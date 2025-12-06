/**
 * Cosmos RPi Dev Board - ESP32-S3 Firmware
 *
 * This firmware handles:
 * 1. WiFi connectivity
 * 2. HTTP API for receiving commands from GitHub Actions
 * 3. SPI communication with STM32H563 main controller
 *
 * Build with ESP-IDF:
 *   idf.py set-target esp32s3
 *   idf.py build
 *   idf.py flash
 */

#include <stdio.h>
#include <string.h>
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "freertos/event_groups.h"
#include "esp_system.h"
#include "esp_wifi.h"
#include "esp_event.h"
#include "esp_log.h"
#include "esp_http_server.h"
#include "esp_spiffs.h"
#include "nvs_flash.h"
#include "driver/spi_master.h"
#include "driver/gpio.h"

static const char *TAG = "cosmos-rpi";

// WiFi credentials (set via menuconfig or NVS)
#define WIFI_SSID      CONFIG_WIFI_SSID
#define WIFI_PASSWORD  CONFIG_WIFI_PASSWORD

// SPI pins for STM32 communication
#define SPI_MOSI_PIN   GPIO_NUM_11
#define SPI_MISO_PIN   GPIO_NUM_13
#define SPI_SCLK_PIN   GPIO_NUM_12
#define SPI_CS_PIN     GPIO_NUM_10

// LED pins
#define LED_POWER      GPIO_NUM_4
#define LED_WIFI       GPIO_NUM_5
#define LED_JOB        GPIO_NUM_6
#define LED_TEST       GPIO_NUM_7
#define LED_RESULT     GPIO_NUM_8

// SPI Commands (matching protocol spec)
#define CMD_PING         0x01
#define CMD_UPLOAD_START 0x02
#define CMD_UPLOAD_DATA  0x03
#define CMD_UPLOAD_END   0x04
#define CMD_RUN_TEST     0x05
#define CMD_GET_STATUS   0x06
#define CMD_GET_LOG      0x07
#define CMD_RESET        0x08

// SPI Responses
#define RSP_OK           0x10
#define RSP_ERROR        0x11
#define RSP_BUSY         0x12
#define RSP_DATA         0x13
#define RSP_STATUS       0x14

// Board states
typedef enum {
    STATE_IDLE = 0x00,
    STATE_UPLOADING = 0x01,
    STATE_FLASHING = 0x02,
    STATE_BOOTING = 0x03,
    STATE_RUNNING = 0x04,
    STATE_COMPLETED = 0x05,
    STATE_ERROR = 0xFF
} board_state_t;

// Global state
static board_state_t g_state = STATE_IDLE;
static uint8_t g_progress = 0;
static char g_message[128] = "Ready";
static uint8_t *g_uart_log = NULL;
static size_t g_uart_log_size = 0;
static spi_device_handle_t g_spi = NULL;

// WiFi event group
static EventGroupHandle_t s_wifi_event_group;
#define WIFI_CONNECTED_BIT BIT0
#define WIFI_FAIL_BIT      BIT1

// Forward declarations
static esp_err_t status_handler(httpd_req_t *req);
static esp_err_t upload_handler(httpd_req_t *req);
static esp_err_t run_handler(httpd_req_t *req);
static esp_err_t log_handler(httpd_req_t *req);
static esp_err_t reset_handler(httpd_req_t *req);

/**
 * Initialize LEDs
 */
static void init_leds(void)
{
    gpio_config_t io_conf = {
        .pin_bit_mask = (1ULL << LED_POWER) | (1ULL << LED_WIFI) |
                       (1ULL << LED_JOB) | (1ULL << LED_TEST) | (1ULL << LED_RESULT),
        .mode = GPIO_MODE_OUTPUT,
        .pull_up_en = GPIO_PULLUP_DISABLE,
        .pull_down_en = GPIO_PULLDOWN_DISABLE,
        .intr_type = GPIO_INTR_DISABLE
    };
    gpio_config(&io_conf);

    // Power LED on
    gpio_set_level(LED_POWER, 1);
}

/**
 * Set LED state
 */
static void set_led(gpio_num_t led, bool on)
{
    gpio_set_level(led, on ? 1 : 0);
}

/**
 * Initialize SPI for STM32 communication
 */
static esp_err_t init_spi(void)
{
    spi_bus_config_t buscfg = {
        .mosi_io_num = SPI_MOSI_PIN,
        .miso_io_num = SPI_MISO_PIN,
        .sclk_io_num = SPI_SCLK_PIN,
        .quadwp_io_num = -1,
        .quadhd_io_num = -1,
        .max_transfer_sz = 64 * 1024
    };

    spi_device_interface_config_t devcfg = {
        .clock_speed_hz = 10 * 1000 * 1000,  // 10 MHz
        .mode = 0,
        .spics_io_num = SPI_CS_PIN,
        .queue_size = 7
    };

    ESP_ERROR_CHECK(spi_bus_initialize(SPI2_HOST, &buscfg, SPI_DMA_CH_AUTO));
    ESP_ERROR_CHECK(spi_bus_add_device(SPI2_HOST, &devcfg, &g_spi));

    return ESP_OK;
}

/**
 * Send command to STM32 via SPI
 */
static esp_err_t spi_send_command(uint8_t cmd, const uint8_t *data, size_t len,
                                  uint8_t *rsp_code, uint8_t *rsp_data, size_t *rsp_len)
{
    // Build command packet: [CMD:1][LEN:4][DATA:N]
    size_t tx_len = 1 + 4 + len;
    uint8_t *tx_buf = malloc(tx_len);
    if (!tx_buf) return ESP_ERR_NO_MEM;

    tx_buf[0] = cmd;
    tx_buf[1] = (len >> 0) & 0xFF;
    tx_buf[2] = (len >> 8) & 0xFF;
    tx_buf[3] = (len >> 16) & 0xFF;
    tx_buf[4] = (len >> 24) & 0xFF;
    if (len > 0 && data) {
        memcpy(tx_buf + 5, data, len);
    }

    // Allocate response buffer (expecting: [RSP:1][LEN:4][DATA:N])
    size_t rx_max = 1 + 4 + 4096;  // Max 4KB response
    uint8_t *rx_buf = malloc(rx_max);
    if (!rx_buf) {
        free(tx_buf);
        return ESP_ERR_NO_MEM;
    }

    spi_transaction_t t = {
        .length = tx_len * 8,
        .tx_buffer = tx_buf,
        .rx_buffer = rx_buf,
        .rxlength = rx_max * 8
    };

    esp_err_t ret = spi_device_transmit(g_spi, &t);
    if (ret != ESP_OK) {
        free(tx_buf);
        free(rx_buf);
        return ret;
    }

    // Parse response
    *rsp_code = rx_buf[0];
    uint32_t rsp_data_len = rx_buf[1] | (rx_buf[2] << 8) |
                           (rx_buf[3] << 16) | (rx_buf[4] << 24);

    if (rsp_data && rsp_len) {
        size_t copy_len = (rsp_data_len < *rsp_len) ? rsp_data_len : *rsp_len;
        memcpy(rsp_data, rx_buf + 5, copy_len);
        *rsp_len = rsp_data_len;
    }

    free(tx_buf);
    free(rx_buf);
    return ESP_OK;
}

/**
 * WiFi event handler
 */
static void wifi_event_handler(void *arg, esp_event_base_t event_base,
                               int32_t event_id, void *event_data)
{
    if (event_base == WIFI_EVENT && event_id == WIFI_EVENT_STA_START) {
        esp_wifi_connect();
    } else if (event_base == WIFI_EVENT && event_id == WIFI_EVENT_STA_DISCONNECTED) {
        set_led(LED_WIFI, false);
        esp_wifi_connect();
        ESP_LOGI(TAG, "Reconnecting to WiFi...");
    } else if (event_base == IP_EVENT && event_id == IP_EVENT_STA_GOT_IP) {
        ip_event_got_ip_t *event = (ip_event_got_ip_t *)event_data;
        ESP_LOGI(TAG, "Got IP: " IPSTR, IP2STR(&event->ip_info.ip));
        set_led(LED_WIFI, true);
        xEventGroupSetBits(s_wifi_event_group, WIFI_CONNECTED_BIT);
    }
}

/**
 * Initialize WiFi
 */
static void init_wifi(void)
{
    s_wifi_event_group = xEventGroupCreate();

    ESP_ERROR_CHECK(esp_netif_init());
    ESP_ERROR_CHECK(esp_event_loop_create_default());
    esp_netif_create_default_wifi_sta();

    wifi_init_config_t cfg = WIFI_INIT_CONFIG_DEFAULT();
    ESP_ERROR_CHECK(esp_wifi_init(&cfg));

    ESP_ERROR_CHECK(esp_event_handler_register(WIFI_EVENT, ESP_EVENT_ANY_ID,
                                               &wifi_event_handler, NULL));
    ESP_ERROR_CHECK(esp_event_handler_register(IP_EVENT, IP_EVENT_STA_GOT_IP,
                                               &wifi_event_handler, NULL));

    wifi_config_t wifi_config = {
        .sta = {
            .ssid = WIFI_SSID,
            .password = WIFI_PASSWORD,
            .threshold.authmode = WIFI_AUTH_WPA2_PSK,
        },
    };
    ESP_ERROR_CHECK(esp_wifi_set_mode(WIFI_MODE_STA));
    ESP_ERROR_CHECK(esp_wifi_set_config(WIFI_IF_STA, &wifi_config));
    ESP_ERROR_CHECK(esp_wifi_start());

    ESP_LOGI(TAG, "WiFi initialized, connecting to %s...", WIFI_SSID);
}

/**
 * HTTP GET /status handler
 */
static esp_err_t status_handler(httpd_req_t *req)
{
    char response[256];
    snprintf(response, sizeof(response),
             "{\"state\":\"%s\",\"progress\":%d,\"message\":\"%s\"}",
             g_state == STATE_IDLE ? "idle" :
             g_state == STATE_UPLOADING ? "uploading" :
             g_state == STATE_FLASHING ? "flashing" :
             g_state == STATE_BOOTING ? "booting" :
             g_state == STATE_RUNNING ? "running" :
             g_state == STATE_COMPLETED ? "completed" : "error",
             g_progress, g_message);

    httpd_resp_set_type(req, "application/json");
    return httpd_resp_send(req, response, strlen(response));
}

/**
 * HTTP POST /upload handler
 */
static esp_err_t upload_handler(httpd_req_t *req)
{
    if (g_state != STATE_IDLE) {
        httpd_resp_send_err(req, HTTPD_400_BAD_REQUEST, "Board is busy");
        return ESP_FAIL;
    }

    g_state = STATE_UPLOADING;
    set_led(LED_JOB, true);
    snprintf(g_message, sizeof(g_message), "Receiving ISO...");

    // Get content length
    size_t content_len = req->content_len;
    ESP_LOGI(TAG, "Receiving ISO upload: %zu bytes", content_len);

    // Send upload start to STM32
    uint8_t size_buf[4];
    size_buf[0] = (content_len >> 0) & 0xFF;
    size_buf[1] = (content_len >> 8) & 0xFF;
    size_buf[2] = (content_len >> 16) & 0xFF;
    size_buf[3] = (content_len >> 24) & 0xFF;

    uint8_t rsp_code;
    size_t rsp_len = 0;
    spi_send_command(CMD_UPLOAD_START, size_buf, 4, &rsp_code, NULL, &rsp_len);

    if (rsp_code != RSP_OK) {
        g_state = STATE_ERROR;
        snprintf(g_message, sizeof(g_message), "STM32 rejected upload");
        httpd_resp_send_err(req, HTTPD_500_INTERNAL_SERVER_ERROR, "STM32 error");
        return ESP_FAIL;
    }

    // Receive and forward data in chunks
    uint8_t *chunk = malloc(64 * 1024);
    if (!chunk) {
        g_state = STATE_ERROR;
        httpd_resp_send_err(req, HTTPD_500_INTERNAL_SERVER_ERROR, "Out of memory");
        return ESP_ERR_NO_MEM;
    }

    size_t received = 0;
    while (received < content_len) {
        size_t to_read = content_len - received;
        if (to_read > 64 * 1024) to_read = 64 * 1024;

        int ret = httpd_req_recv(req, (char *)chunk, to_read);
        if (ret <= 0) {
            free(chunk);
            g_state = STATE_ERROR;
            httpd_resp_send_err(req, HTTPD_500_INTERNAL_SERVER_ERROR, "Upload failed");
            return ESP_FAIL;
        }

        // Forward to STM32
        spi_send_command(CMD_UPLOAD_DATA, chunk, ret, &rsp_code, NULL, &rsp_len);
        if (rsp_code != RSP_OK) {
            free(chunk);
            g_state = STATE_ERROR;
            httpd_resp_send_err(req, HTTPD_500_INTERNAL_SERVER_ERROR, "STM32 write error");
            return ESP_FAIL;
        }

        received += ret;
        g_progress = (received * 100) / content_len;
        ESP_LOGI(TAG, "Upload progress: %d%%", g_progress);
    }

    free(chunk);

    // Finish upload
    spi_send_command(CMD_UPLOAD_END, NULL, 0, &rsp_code, NULL, &rsp_len);
    if (rsp_code != RSP_OK) {
        g_state = STATE_ERROR;
        snprintf(g_message, sizeof(g_message), "Checksum verification failed");
        httpd_resp_send_err(req, HTTPD_500_INTERNAL_SERVER_ERROR, "Checksum error");
        return ESP_FAIL;
    }

    g_state = STATE_IDLE;
    g_progress = 100;
    snprintf(g_message, sizeof(g_message), "Upload complete");
    set_led(LED_JOB, false);

    httpd_resp_set_type(req, "application/json");
    return httpd_resp_send(req, "{\"success\":true}", 16);
}

/**
 * HTTP POST /run handler
 */
static esp_err_t run_handler(httpd_req_t *req)
{
    if (g_state != STATE_IDLE) {
        httpd_resp_send_err(req, HTTPD_400_BAD_REQUEST, "Board is busy");
        return ESP_FAIL;
    }

    g_state = STATE_BOOTING;
    g_progress = 0;
    set_led(LED_JOB, true);
    set_led(LED_TEST, true);
    snprintf(g_message, sizeof(g_message), "Starting test...");

    // Tell STM32 to start test
    uint8_t rsp_code;
    size_t rsp_len = 0;
    spi_send_command(CMD_RUN_TEST, NULL, 0, &rsp_code, NULL, &rsp_len);

    if (rsp_code != RSP_OK) {
        g_state = STATE_ERROR;
        snprintf(g_message, sizeof(g_message), "Failed to start test");
        httpd_resp_send_err(req, HTTPD_500_INTERNAL_SERVER_ERROR, "Start failed");
        return ESP_FAIL;
    }

    httpd_resp_set_type(req, "application/json");
    return httpd_resp_send(req, "{\"success\":true}", 16);
}

/**
 * HTTP GET /uart-log handler
 */
static esp_err_t log_handler(httpd_req_t *req)
{
    // Get log from STM32
    uint8_t rsp_code;
    uint8_t *log_data = malloc(64 * 1024);
    size_t log_len = 64 * 1024;

    if (!log_data) {
        httpd_resp_send_err(req, HTTPD_500_INTERNAL_SERVER_ERROR, "Out of memory");
        return ESP_ERR_NO_MEM;
    }

    spi_send_command(CMD_GET_LOG, NULL, 0, &rsp_code, log_data, &log_len);

    if (rsp_code != RSP_DATA) {
        free(log_data);
        httpd_resp_send_err(req, HTTPD_500_INTERNAL_SERVER_ERROR, "Failed to get log");
        return ESP_FAIL;
    }

    httpd_resp_set_type(req, "text/plain");
    esp_err_t ret = httpd_resp_send(req, (char *)log_data, log_len);
    free(log_data);
    return ret;
}

/**
 * HTTP POST /reset handler
 */
static esp_err_t reset_handler(httpd_req_t *req)
{
    uint8_t rsp_code;
    size_t rsp_len = 0;
    spi_send_command(CMD_RESET, NULL, 0, &rsp_code, NULL, &rsp_len);

    g_state = STATE_IDLE;
    g_progress = 0;
    snprintf(g_message, sizeof(g_message), "Ready");
    set_led(LED_JOB, false);
    set_led(LED_TEST, false);
    set_led(LED_RESULT, false);

    httpd_resp_set_type(req, "application/json");
    return httpd_resp_send(req, "{\"success\":true}", 16);
}

/**
 * Start HTTP server
 */
static httpd_handle_t start_webserver(void)
{
    httpd_config_t config = HTTPD_DEFAULT_CONFIG();
    httpd_handle_t server = NULL;

    if (httpd_start(&server, &config) == ESP_OK) {
        httpd_uri_t status_uri = {
            .uri = "/status",
            .method = HTTP_GET,
            .handler = status_handler
        };
        httpd_register_uri_handler(server, &status_uri);

        httpd_uri_t upload_uri = {
            .uri = "/upload",
            .method = HTTP_POST,
            .handler = upload_handler
        };
        httpd_register_uri_handler(server, &upload_uri);

        httpd_uri_t run_uri = {
            .uri = "/run",
            .method = HTTP_POST,
            .handler = run_handler
        };
        httpd_register_uri_handler(server, &run_uri);

        httpd_uri_t log_uri = {
            .uri = "/uart-log",
            .method = HTTP_GET,
            .handler = log_handler
        };
        httpd_register_uri_handler(server, &log_uri);

        httpd_uri_t reset_uri = {
            .uri = "/reset",
            .method = HTTP_POST,
            .handler = reset_handler
        };
        httpd_register_uri_handler(server, &reset_uri);

        ESP_LOGI(TAG, "HTTP server started on port %d", config.server_port);
    }

    return server;
}

/**
 * Status polling task - updates state from STM32
 */
static void status_task(void *pvParameters)
{
    while (1) {
        if (g_state == STATE_BOOTING || g_state == STATE_RUNNING) {
            uint8_t rsp_code;
            uint8_t status_data[128];
            size_t status_len = sizeof(status_data);

            spi_send_command(CMD_GET_STATUS, NULL, 0, &rsp_code, status_data, &status_len);

            if (rsp_code == RSP_STATUS && status_len >= 1) {
                board_state_t new_state = (board_state_t)status_data[0];
                uint8_t new_progress = status_len >= 2 ? status_data[1] : 0;

                if (new_state != g_state) {
                    ESP_LOGI(TAG, "State changed: %d -> %d", g_state, new_state);
                    g_state = new_state;

                    // Update LEDs based on state
                    if (new_state == STATE_COMPLETED) {
                        set_led(LED_TEST, false);
                        set_led(LED_RESULT, true);  // Green for pass
                        set_led(LED_JOB, false);
                    } else if (new_state == STATE_ERROR) {
                        set_led(LED_TEST, false);
                        set_led(LED_RESULT, true);  // Would be red in real hardware
                        set_led(LED_JOB, false);
                    }
                }
                g_progress = new_progress;
            }
        }

        vTaskDelay(pdMS_TO_TICKS(500));
    }
}

/**
 * Main entry point
 */
void app_main(void)
{
    ESP_LOGI(TAG, "Cosmos RPi Dev Board - ESP32 Firmware");
    ESP_LOGI(TAG, "Initializing...");

    // Initialize NVS
    esp_err_t ret = nvs_flash_init();
    if (ret == ESP_ERR_NVS_NO_FREE_PAGES || ret == ESP_ERR_NVS_NEW_VERSION_FOUND) {
        ESP_ERROR_CHECK(nvs_flash_erase());
        ret = nvs_flash_init();
    }
    ESP_ERROR_CHECK(ret);

    // Initialize hardware
    init_leds();
    ESP_ERROR_CHECK(init_spi());
    init_wifi();

    // Wait for WiFi connection
    xEventGroupWaitBits(s_wifi_event_group, WIFI_CONNECTED_BIT,
                        pdFALSE, pdFALSE, portMAX_DELAY);

    // Start HTTP server
    start_webserver();

    // Start status polling task
    xTaskCreate(status_task, "status_task", 4096, NULL, 5, NULL);

    ESP_LOGI(TAG, "Initialization complete. Ready for commands.");
}
