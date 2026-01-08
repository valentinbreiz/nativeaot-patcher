/**
 * Cosmos RPi Dev Board - STM32H563 Firmware
 *
 * This firmware handles:
 * 1. SPI slave communication with ESP32 for receiving ISO files
 * 2. SDMMC interface for storing kernel ISO to MicroSD
 * 3. UART communication with Raspberry Pi for test protocol
 * 4. GPIO control for Raspberry Pi power/reset
 * 5. Ethernet (optional) for TFTP server
 *
 * Pin Configuration (from KiCad schematic CosmosRpiDevBoard.kicad_sch):
 *
 * SPI1 (Slave to ESP32 SPI1):
 *   PA3  - SPI1_NSS  (Chip Select from ESP32 GPIO9)
 *   PA6  - SPI1_MISO (Data out to ESP32 GPIO12)
 *   PB3  - SPI1_SCK  (Clock from ESP32 GPIO11)
 *   PB5  - SPI1_MOSI (Data in from ESP32 GPIO10)
 *
 * SPI2 (Slave to ESP32 SPI2):
 *   PA4  - SPI2_NSS  (Chip Select from ESP32 GPIO5)
 *   PB13 - SPI2_SCK  (Clock from ESP32 GPIO7)
 *   PC2  - SPI2_MISO (Data out to ESP32 GPIO8)
 *   PC3  - SPI2_MOSI (Data in from ESP32 GPIO6)
 *
 * USART1 (to Raspberry Pi):
 *   PA9  - USART1_TX
 *   PA10 - USART1_RX
 *
 * USART6 (Debug):
 *   PC6  - USART6_TX
 *   PC7  - USART6_RX
 *
 * SDMMC1 (MicroSD):
 *   PC8  - SDMMC1_D0
 *   PC9  - SDMMC1_D1
 *   PC10 - SDMMC1_D2
 *   PC11 - SDMMC1_D3
 *   PC12 - SDMMC1_CK
 *   PD2  - SDMMC1_CMD
 *   PA15 - SDMMC_CD (Card Detect)
 *
 * Ethernet RMII:
 *   PA1  - ETH_REF_CLK
 *   PA2  - ETH_MDIO
 *   PA5  - ETH_TX_EN
 *   PA7  - ETH_CRS_DV
 *   PB12 - ETH_TXD0
 *   PB15 - ETH_TXD1
 *   PC1  - ETH_MDC
 *   PC4  - ETH_RXD0
 *   PC5  - ETH_RXD1
 *   PB10 - ETH_INT
 *
 * GPIO:
 *   PB2  - ETH_LED_Y (Yellow LED)
 *   PB6  - STATUS_LED
 *   PB7  - EXT_RST (External Reset - RPi power control)
 *   PB8  - EXT_BOOT (External Boot control)
 *
 * Build with STM32CubeIDE or CMake with arm-none-eabi-gcc
 */

#include "stm32h5xx_hal.h"
#include <stdint.h>
#include <stdbool.h>
#include <string.h>

/* ========================= Pin Definitions ========================= */

/* SPI1 Slave pins (from ESP32) */
#define SPI1_NSS_PIN        GPIO_PIN_3
#define SPI1_NSS_PORT       GPIOA
#define SPI1_MISO_PIN       GPIO_PIN_6
#define SPI1_MISO_PORT      GPIOA
#define SPI1_SCK_PIN        GPIO_PIN_3
#define SPI1_SCK_PORT       GPIOB
#define SPI1_MOSI_PIN       GPIO_PIN_5
#define SPI1_MOSI_PORT      GPIOB

/* SPI2 Slave pins (from ESP32) */
#define SPI2_NSS_PIN        GPIO_PIN_4
#define SPI2_NSS_PORT       GPIOA
#define SPI2_SCK_PIN        GPIO_PIN_13
#define SPI2_SCK_PORT       GPIOB
#define SPI2_MISO_PIN       GPIO_PIN_2
#define SPI2_MISO_PORT      GPIOC
#define SPI2_MOSI_PIN       GPIO_PIN_3
#define SPI2_MOSI_PORT      GPIOC

/* USART1 pins (to RPi) */
#define USART1_TX_PIN       GPIO_PIN_9
#define USART1_TX_PORT      GPIOA
#define USART1_RX_PIN       GPIO_PIN_10
#define USART1_RX_PORT      GPIOA

/* USART6 pins (Debug) */
#define USART6_TX_PIN       GPIO_PIN_6
#define USART6_TX_PORT      GPIOC
#define USART6_RX_PIN       GPIO_PIN_7
#define USART6_RX_PORT      GPIOC

/* SDMMC1 pins */
#define SDMMC1_D0_PIN       GPIO_PIN_8
#define SDMMC1_D1_PIN       GPIO_PIN_9
#define SDMMC1_D2_PIN       GPIO_PIN_10
#define SDMMC1_D3_PIN       GPIO_PIN_11
#define SDMMC1_CK_PIN       GPIO_PIN_12
#define SDMMC1_DATA_PORT    GPIOC
#define SDMMC1_CMD_PIN      GPIO_PIN_2
#define SDMMC1_CMD_PORT     GPIOD
#define SDMMC1_CD_PIN       GPIO_PIN_15
#define SDMMC1_CD_PORT      GPIOA

/* GPIO Control pins */
#define STATUS_LED_PIN      GPIO_PIN_6
#define STATUS_LED_PORT     GPIOB
#define ETH_LED_Y_PIN       GPIO_PIN_2
#define ETH_LED_Y_PORT      GPIOB
#define EXT_RST_PIN         GPIO_PIN_7   /* RPi power/reset control */
#define EXT_RST_PORT        GPIOB
#define EXT_BOOT_PIN        GPIO_PIN_8   /* RPi boot control */
#define EXT_BOOT_PORT       GPIOB

/* ========================= SPI Protocol ========================= */

/* SPI Commands (from ESP32) */
#define CMD_PING            0x01
#define CMD_UPLOAD_START    0x02
#define CMD_UPLOAD_DATA     0x03
#define CMD_UPLOAD_END      0x04
#define CMD_RUN_TEST        0x05
#define CMD_GET_STATUS      0x06
#define CMD_GET_LOG         0x07
#define CMD_RESET           0x08

/* SPI Responses (to ESP32) */
#define RSP_OK              0x10
#define RSP_ERROR           0x11
#define RSP_BUSY            0x12
#define RSP_DATA            0x13
#define RSP_STATUS          0x14

/* ========================= Board State ========================= */

typedef enum {
    STATE_IDLE       = 0x00,
    STATE_UPLOADING  = 0x01,
    STATE_FLASHING   = 0x02,
    STATE_BOOTING    = 0x03,
    STATE_RUNNING    = 0x04,
    STATE_COMPLETED  = 0x05,
    STATE_ERROR      = 0xFF
} BoardState;

/* ========================= UART Protocol ========================= */

/* Test protocol commands (from RPi) */
#define UART_CMD_TEST_SUITE_START   100
#define UART_CMD_TEST_START         101
#define UART_CMD_TEST_PASS          102
#define UART_CMD_TEST_FAIL          103
#define UART_CMD_TEST_SKIP          104
#define UART_CMD_TEST_SUITE_END     105

/* End marker */
static const uint8_t UART_END_MARKER[] = {0xDE, 0xAD, 0xBE, 0xEF, 0xCA, 0xFE, 0xBA, 0xBE};

/* ========================= Global State ========================= */

static BoardState g_state = STATE_IDLE;
static uint8_t g_progress = 0;
static char g_message[128] = "Ready";

/* UART log buffer (circular) */
#define UART_LOG_SIZE   (64 * 1024)
static uint8_t g_uart_log[UART_LOG_SIZE];
static volatile uint32_t g_uart_log_head = 0;
static volatile uint32_t g_uart_log_tail = 0;

/* ISO receive buffer */
static uint32_t g_iso_expected_size = 0;
static uint32_t g_iso_received_size = 0;

/* HAL Handles */
static SPI_HandleTypeDef hspi1;
static UART_HandleTypeDef huart1;
static UART_HandleTypeDef huart6;
static SD_HandleTypeDef hsd1;

/* ========================= GPIO Functions ========================= */

/**
 * Set status LED state
 */
static void set_status_led(bool on)
{
    HAL_GPIO_WritePin(STATUS_LED_PORT, STATUS_LED_PIN, on ? GPIO_PIN_SET : GPIO_PIN_RESET);
}

/**
 * Set ethernet LED state
 */
static void set_eth_led(bool on)
{
    HAL_GPIO_WritePin(ETH_LED_Y_PORT, ETH_LED_Y_PIN, on ? GPIO_PIN_SET : GPIO_PIN_RESET);
}

/**
 * Control Raspberry Pi power
 */
static void set_rpi_power(bool on)
{
    HAL_GPIO_WritePin(EXT_RST_PORT, EXT_RST_PIN, on ? GPIO_PIN_SET : GPIO_PIN_RESET);
}

/**
 * Control Raspberry Pi boot signal
 */
static void set_rpi_boot(bool active)
{
    HAL_GPIO_WritePin(EXT_BOOT_PORT, EXT_BOOT_PIN, active ? GPIO_PIN_SET : GPIO_PIN_RESET);
}

/**
 * Check if SD card is inserted
 */
static bool is_sd_card_present(void)
{
    return HAL_GPIO_ReadPin(SDMMC1_CD_PORT, SDMMC1_CD_PIN) == GPIO_PIN_RESET;
}

/* ========================= Initialization ========================= */

/**
 * Initialize GPIO pins
 */
static void GPIO_Init(void)
{
    GPIO_InitTypeDef GPIO_InitStruct = {0};

    /* Enable GPIO clocks */
    __HAL_RCC_GPIOA_CLK_ENABLE();
    __HAL_RCC_GPIOB_CLK_ENABLE();
    __HAL_RCC_GPIOC_CLK_ENABLE();
    __HAL_RCC_GPIOD_CLK_ENABLE();

    /* Configure LED outputs */
    GPIO_InitStruct.Mode = GPIO_MODE_OUTPUT_PP;
    GPIO_InitStruct.Pull = GPIO_NOPULL;
    GPIO_InitStruct.Speed = GPIO_SPEED_FREQ_LOW;

    /* Status LED (PB6) */
    GPIO_InitStruct.Pin = STATUS_LED_PIN;
    HAL_GPIO_Init(STATUS_LED_PORT, &GPIO_InitStruct);

    /* Ethernet LED (PB2) */
    GPIO_InitStruct.Pin = ETH_LED_Y_PIN;
    HAL_GPIO_Init(ETH_LED_Y_PORT, &GPIO_InitStruct);

    /* RPi Reset/Power control (PB7) */
    GPIO_InitStruct.Pin = EXT_RST_PIN;
    HAL_GPIO_Init(EXT_RST_PORT, &GPIO_InitStruct);

    /* RPi Boot control (PB8) */
    GPIO_InitStruct.Pin = EXT_BOOT_PIN;
    HAL_GPIO_Init(EXT_BOOT_PORT, &GPIO_InitStruct);

    /* SD Card Detect (PA15) - input with pull-up */
    GPIO_InitStruct.Mode = GPIO_MODE_INPUT;
    GPIO_InitStruct.Pull = GPIO_PULLUP;
    GPIO_InitStruct.Pin = SDMMC1_CD_PIN;
    HAL_GPIO_Init(SDMMC1_CD_PORT, &GPIO_InitStruct);

    /* Initial states */
    set_status_led(true);   /* LED on to indicate power */
    set_eth_led(false);
    set_rpi_power(false);   /* RPi off initially */
    set_rpi_boot(false);
}

/**
 * Initialize SPI1 as slave
 */
static void SPI1_Slave_Init(void)
{
    GPIO_InitTypeDef GPIO_InitStruct = {0};

    __HAL_RCC_SPI1_CLK_ENABLE();

    /* SPI1 GPIO Configuration */
    /* PA3 - NSS, PA6 - MISO */
    GPIO_InitStruct.Pin = SPI1_NSS_PIN | SPI1_MISO_PIN;
    GPIO_InitStruct.Mode = GPIO_MODE_AF_PP;
    GPIO_InitStruct.Pull = GPIO_NOPULL;
    GPIO_InitStruct.Speed = GPIO_SPEED_FREQ_HIGH;
    GPIO_InitStruct.Alternate = GPIO_AF5_SPI1;
    HAL_GPIO_Init(GPIOA, &GPIO_InitStruct);

    /* PB3 - SCK, PB5 - MOSI */
    GPIO_InitStruct.Pin = SPI1_SCK_PIN | SPI1_MOSI_PIN;
    HAL_GPIO_Init(GPIOB, &GPIO_InitStruct);

    /* SPI1 configuration */
    hspi1.Instance = SPI1;
    hspi1.Init.Mode = SPI_MODE_SLAVE;
    hspi1.Init.Direction = SPI_DIRECTION_2LINES;
    hspi1.Init.DataSize = SPI_DATASIZE_8BIT;
    hspi1.Init.CLKPolarity = SPI_POLARITY_LOW;
    hspi1.Init.CLKPhase = SPI_PHASE_1EDGE;
    hspi1.Init.NSS = SPI_NSS_HARD_INPUT;
    hspi1.Init.FirstBit = SPI_FIRSTBIT_MSB;
    hspi1.Init.TIMode = SPI_TIMODE_DISABLE;
    hspi1.Init.CRCCalculation = SPI_CRCCALCULATION_DISABLE;

    HAL_SPI_Init(&hspi1);

    /* Enable SPI interrupt */
    HAL_NVIC_SetPriority(SPI1_IRQn, 1, 0);
    HAL_NVIC_EnableIRQ(SPI1_IRQn);
}

/**
 * Initialize USART1 for RPi communication
 */
static void USART1_Init(void)
{
    GPIO_InitTypeDef GPIO_InitStruct = {0};

    __HAL_RCC_USART1_CLK_ENABLE();

    /* USART1 GPIO Configuration: PA9=TX, PA10=RX */
    GPIO_InitStruct.Pin = USART1_TX_PIN | USART1_RX_PIN;
    GPIO_InitStruct.Mode = GPIO_MODE_AF_PP;
    GPIO_InitStruct.Pull = GPIO_NOPULL;
    GPIO_InitStruct.Speed = GPIO_SPEED_FREQ_HIGH;
    GPIO_InitStruct.Alternate = GPIO_AF7_USART1;
    HAL_GPIO_Init(GPIOA, &GPIO_InitStruct);

    /* USART1 configuration: 115200 8N1 */
    huart1.Instance = USART1;
    huart1.Init.BaudRate = 115200;
    huart1.Init.WordLength = UART_WORDLENGTH_8B;
    huart1.Init.StopBits = UART_STOPBITS_1;
    huart1.Init.Parity = UART_PARITY_NONE;
    huart1.Init.Mode = UART_MODE_TX_RX;
    huart1.Init.HwFlowCtl = UART_HWCONTROL_NONE;
    huart1.Init.OverSampling = UART_OVERSAMPLING_16;

    HAL_UART_Init(&huart1);

    /* Enable UART RX interrupt */
    HAL_NVIC_SetPriority(USART1_IRQn, 2, 0);
    HAL_NVIC_EnableIRQ(USART1_IRQn);
    __HAL_UART_ENABLE_IT(&huart1, UART_IT_RXNE);
}

/**
 * Initialize USART6 for debug output
 */
static void USART6_Debug_Init(void)
{
    GPIO_InitTypeDef GPIO_InitStruct = {0};

    __HAL_RCC_USART6_CLK_ENABLE();

    /* USART6 GPIO Configuration: PC6=TX, PC7=RX */
    GPIO_InitStruct.Pin = USART6_TX_PIN | USART6_RX_PIN;
    GPIO_InitStruct.Mode = GPIO_MODE_AF_PP;
    GPIO_InitStruct.Pull = GPIO_NOPULL;
    GPIO_InitStruct.Speed = GPIO_SPEED_FREQ_HIGH;
    GPIO_InitStruct.Alternate = GPIO_AF7_USART6;
    HAL_GPIO_Init(GPIOC, &GPIO_InitStruct);

    /* USART6 configuration: 115200 8N1 */
    huart6.Instance = USART6;
    huart6.Init.BaudRate = 115200;
    huart6.Init.WordLength = UART_WORDLENGTH_8B;
    huart6.Init.StopBits = UART_STOPBITS_1;
    huart6.Init.Parity = UART_PARITY_NONE;
    huart6.Init.Mode = UART_MODE_TX_RX;
    huart6.Init.HwFlowCtl = UART_HWCONTROL_NONE;
    huart6.Init.OverSampling = UART_OVERSAMPLING_16;

    HAL_UART_Init(&huart6);
}

/**
 * Initialize SDMMC1 for MicroSD
 */
static void SDMMC1_Init(void)
{
    GPIO_InitTypeDef GPIO_InitStruct = {0};

    __HAL_RCC_SDMMC1_CLK_ENABLE();

    /* SDMMC1 GPIO Configuration */
    /* PC8-PC12: D0-D3, CK */
    GPIO_InitStruct.Pin = SDMMC1_D0_PIN | SDMMC1_D1_PIN | SDMMC1_D2_PIN |
                          SDMMC1_D3_PIN | SDMMC1_CK_PIN;
    GPIO_InitStruct.Mode = GPIO_MODE_AF_PP;
    GPIO_InitStruct.Pull = GPIO_PULLUP;
    GPIO_InitStruct.Speed = GPIO_SPEED_FREQ_VERY_HIGH;
    GPIO_InitStruct.Alternate = GPIO_AF12_SDMMC1;
    HAL_GPIO_Init(GPIOC, &GPIO_InitStruct);

    /* PD2: CMD */
    GPIO_InitStruct.Pin = SDMMC1_CMD_PIN;
    HAL_GPIO_Init(GPIOD, &GPIO_InitStruct);

    /* SDMMC1 configuration */
    hsd1.Instance = SDMMC1;
    hsd1.Init.ClockEdge = SDMMC_CLOCK_EDGE_RISING;
    hsd1.Init.ClockPowerSave = SDMMC_CLOCK_POWER_SAVE_DISABLE;
    hsd1.Init.BusWide = SDMMC_BUS_WIDE_4B;
    hsd1.Init.HardwareFlowControl = SDMMC_HARDWARE_FLOW_CONTROL_DISABLE;
    hsd1.Init.ClockDiv = 2;

    /* Note: HAL_SD_Init() should be called after card is detected */
}

/* ========================= Debug Output ========================= */

/**
 * Send debug message via USART6
 */
static void debug_print(const char *msg)
{
    HAL_UART_Transmit(&huart6, (uint8_t *)msg, strlen(msg), HAL_MAX_DELAY);
}

/**
 * Send debug message with newline
 */
static void debug_println(const char *msg)
{
    debug_print(msg);
    debug_print("\r\n");
}

/* ========================= UART Log Buffer ========================= */

/**
 * Add byte to UART log buffer
 */
static void uart_log_push(uint8_t byte)
{
    uint32_t next = (g_uart_log_head + 1) % UART_LOG_SIZE;
    if (next != g_uart_log_tail) {
        g_uart_log[g_uart_log_head] = byte;
        g_uart_log_head = next;
    }
}

/**
 * Get bytes from UART log buffer
 */
static uint32_t uart_log_read(uint8_t *buf, uint32_t max_len)
{
    uint32_t count = 0;
    while (g_uart_log_tail != g_uart_log_head && count < max_len) {
        buf[count++] = g_uart_log[g_uart_log_tail];
        g_uart_log_tail = (g_uart_log_tail + 1) % UART_LOG_SIZE;
    }
    return count;
}

/**
 * Check for end marker in UART log
 */
static bool uart_log_check_end_marker(void)
{
    if ((g_uart_log_head - g_uart_log_tail + UART_LOG_SIZE) % UART_LOG_SIZE < sizeof(UART_END_MARKER)) {
        return false;
    }

    /* Check last 8 bytes for end marker */
    uint32_t pos = (g_uart_log_head - sizeof(UART_END_MARKER) + UART_LOG_SIZE) % UART_LOG_SIZE;
    for (size_t i = 0; i < sizeof(UART_END_MARKER); i++) {
        if (g_uart_log[(pos + i) % UART_LOG_SIZE] != UART_END_MARKER[i]) {
            return false;
        }
    }
    return true;
}

/* ========================= SPI Command Handlers ========================= */

/**
 * Handle PING command
 */
static void handle_cmd_ping(uint8_t *response, uint32_t *rsp_len)
{
    response[0] = RSP_OK;
    *rsp_len = 1;
}

/**
 * Handle UPLOAD_START command
 */
static void handle_cmd_upload_start(const uint8_t *data, uint32_t len,
                                    uint8_t *response, uint32_t *rsp_len)
{
    if (g_state != STATE_IDLE || len < 4) {
        response[0] = RSP_ERROR;
        *rsp_len = 1;
        return;
    }

    /* Extract expected ISO size */
    g_iso_expected_size = data[0] | (data[1] << 8) | (data[2] << 16) | (data[3] << 24);
    g_iso_received_size = 0;

    /* Check SD card */
    if (!is_sd_card_present()) {
        debug_println("Error: SD card not present");
        response[0] = RSP_ERROR;
        *rsp_len = 1;
        return;
    }

    /* Initialize SD card */
    if (HAL_SD_Init(&hsd1) != HAL_OK) {
        debug_println("Error: SD card init failed");
        response[0] = RSP_ERROR;
        *rsp_len = 1;
        return;
    }

    g_state = STATE_UPLOADING;
    g_progress = 0;
    snprintf(g_message, sizeof(g_message), "Receiving %lu bytes", (unsigned long)g_iso_expected_size);
    debug_println(g_message);

    response[0] = RSP_OK;
    *rsp_len = 1;
}

/**
 * Handle UPLOAD_DATA command
 */
static void handle_cmd_upload_data(const uint8_t *data, uint32_t len,
                                   uint8_t *response, uint32_t *rsp_len)
{
    if (g_state != STATE_UPLOADING) {
        response[0] = RSP_ERROR;
        *rsp_len = 1;
        return;
    }

    /* Write data to SD card */
    uint32_t block_addr = g_iso_received_size / 512;
    uint32_t blocks = (len + 511) / 512;

    /* Pad to 512-byte boundary if needed */
    static uint8_t block_buf[512];
    uint32_t remaining = len;
    const uint8_t *src = data;

    while (remaining > 0) {
        uint32_t chunk = (remaining > 512) ? 512 : remaining;
        memcpy(block_buf, src, chunk);
        if (chunk < 512) {
            memset(block_buf + chunk, 0, 512 - chunk);
        }

        if (HAL_SD_WriteBlocks(&hsd1, block_buf, block_addr, 1, HAL_MAX_DELAY) != HAL_OK) {
            debug_println("Error: SD write failed");
            g_state = STATE_ERROR;
            response[0] = RSP_ERROR;
            *rsp_len = 1;
            return;
        }

        block_addr++;
        src += chunk;
        remaining -= chunk;
    }

    g_iso_received_size += len;
    g_progress = (g_iso_received_size * 100) / g_iso_expected_size;

    response[0] = RSP_OK;
    *rsp_len = 1;
}

/**
 * Handle UPLOAD_END command
 */
static void handle_cmd_upload_end(uint8_t *response, uint32_t *rsp_len)
{
    if (g_state != STATE_UPLOADING) {
        response[0] = RSP_ERROR;
        *rsp_len = 1;
        return;
    }

    if (g_iso_received_size != g_iso_expected_size) {
        debug_println("Error: Size mismatch");
        g_state = STATE_ERROR;
        response[0] = RSP_ERROR;
        *rsp_len = 1;
        return;
    }

    g_state = STATE_IDLE;
    g_progress = 100;
    snprintf(g_message, sizeof(g_message), "Upload complete: %lu bytes", (unsigned long)g_iso_received_size);
    debug_println(g_message);

    response[0] = RSP_OK;
    *rsp_len = 1;
}

/**
 * Handle RUN_TEST command
 */
static void handle_cmd_run_test(uint8_t *response, uint32_t *rsp_len)
{
    if (g_state != STATE_IDLE) {
        response[0] = RSP_BUSY;
        *rsp_len = 1;
        return;
    }

    g_state = STATE_BOOTING;
    g_progress = 0;

    /* Clear UART log buffer */
    g_uart_log_head = 0;
    g_uart_log_tail = 0;

    /* Power on Raspberry Pi */
    debug_println("Starting RPi...");
    set_rpi_boot(true);
    HAL_Delay(100);
    set_rpi_power(true);

    g_state = STATE_RUNNING;
    snprintf(g_message, sizeof(g_message), "Running test");

    response[0] = RSP_OK;
    *rsp_len = 1;
}

/**
 * Handle GET_STATUS command
 */
static void handle_cmd_get_status(uint8_t *response, uint32_t *rsp_len)
{
    response[0] = RSP_STATUS;
    response[1] = (uint8_t)g_state;
    response[2] = g_progress;
    *rsp_len = 3;
}

/**
 * Handle GET_LOG command
 */
static void handle_cmd_get_log(uint8_t *response, uint32_t *rsp_len)
{
    response[0] = RSP_DATA;

    /* Copy log data */
    uint32_t log_len = uart_log_read(response + 5, 4096);

    /* Set length in header */
    response[1] = (log_len >> 0) & 0xFF;
    response[2] = (log_len >> 8) & 0xFF;
    response[3] = (log_len >> 16) & 0xFF;
    response[4] = (log_len >> 24) & 0xFF;

    *rsp_len = 5 + log_len;
}

/**
 * Handle RESET command
 */
static void handle_cmd_reset(uint8_t *response, uint32_t *rsp_len)
{
    /* Power off Raspberry Pi */
    set_rpi_power(false);
    set_rpi_boot(false);

    /* Reset state */
    g_state = STATE_IDLE;
    g_progress = 0;
    snprintf(g_message, sizeof(g_message), "Ready");

    /* Clear log buffer */
    g_uart_log_head = 0;
    g_uart_log_tail = 0;

    debug_println("Reset complete");

    response[0] = RSP_OK;
    *rsp_len = 1;
}

/* ========================= SPI Processing ========================= */

/**
 * Process SPI command from ESP32
 */
static void process_spi_command(const uint8_t *cmd_buf, uint32_t cmd_len,
                                uint8_t *rsp_buf, uint32_t *rsp_len)
{
    if (cmd_len < 5) {
        rsp_buf[0] = RSP_ERROR;
        *rsp_len = 1;
        return;
    }

    uint8_t cmd = cmd_buf[0];
    uint32_t data_len = cmd_buf[1] | (cmd_buf[2] << 8) | (cmd_buf[3] << 16) | (cmd_buf[4] << 24);
    const uint8_t *data = cmd_buf + 5;

    switch (cmd) {
        case CMD_PING:
            handle_cmd_ping(rsp_buf, rsp_len);
            break;
        case CMD_UPLOAD_START:
            handle_cmd_upload_start(data, data_len, rsp_buf, rsp_len);
            break;
        case CMD_UPLOAD_DATA:
            handle_cmd_upload_data(data, data_len, rsp_buf, rsp_len);
            break;
        case CMD_UPLOAD_END:
            handle_cmd_upload_end(rsp_buf, rsp_len);
            break;
        case CMD_RUN_TEST:
            handle_cmd_run_test(rsp_buf, rsp_len);
            break;
        case CMD_GET_STATUS:
            handle_cmd_get_status(rsp_buf, rsp_len);
            break;
        case CMD_GET_LOG:
            handle_cmd_get_log(rsp_buf, rsp_len);
            break;
        case CMD_RESET:
            handle_cmd_reset(rsp_buf, rsp_len);
            break;
        default:
            rsp_buf[0] = RSP_ERROR;
            *rsp_len = 1;
            break;
    }
}

/* ========================= Interrupt Handlers ========================= */

/**
 * USART1 IRQ Handler - receives test output from RPi
 */
void USART1_IRQHandler(void)
{
    if (__HAL_UART_GET_FLAG(&huart1, UART_FLAG_RXNE)) {
        uint8_t byte = (uint8_t)(huart1.Instance->RDR & 0xFF);
        uart_log_push(byte);

        /* Check for end marker */
        if (uart_log_check_end_marker()) {
            g_state = STATE_COMPLETED;
            g_progress = 100;
            snprintf(g_message, sizeof(g_message), "Test complete");
        }
    }
}

/* ========================= Main Function ========================= */

/**
 * System Clock Configuration for STM32H563 @ 250MHz
 */
static void SystemClock_Config(void)
{
    /* Configure system clock using HSI and PLL */
    /* This is a placeholder - actual implementation depends on crystal/HSE */
    /* Using HAL_RCC_OscConfig and HAL_RCC_ClockConfig */
}

/**
 * Main entry point
 */
int main(void)
{
    /* HAL initialization */
    HAL_Init();

    /* Configure system clock */
    SystemClock_Config();

    /* Initialize peripherals */
    GPIO_Init();
    SPI1_Slave_Init();
    USART1_Init();
    USART6_Debug_Init();
    SDMMC1_Init();

    debug_println("Cosmos RPi Dev Board - STM32 Firmware");
    debug_println("Initialized. Waiting for commands...");

    /* SPI receive/transmit buffers */
    static uint8_t spi_rx_buf[8192];
    static uint8_t spi_tx_buf[8192];
    uint32_t spi_tx_len = 0;

    /* Main loop */
    while (1) {
        /* Handle SPI communication */
        /* In real implementation, this would be interrupt-driven */
        /* For now, poll-based for simplicity */

        if (HAL_SPI_GetState(&hspi1) == HAL_SPI_STATE_READY) {
            /* Prepare to receive command */
            HAL_SPI_Receive(&hspi1, spi_rx_buf, sizeof(spi_rx_buf), 100);

            if (spi_rx_buf[0] != 0x00 && spi_rx_buf[0] != 0xFF) {
                /* Process received command */
                process_spi_command(spi_rx_buf, sizeof(spi_rx_buf), spi_tx_buf, &spi_tx_len);

                /* Send response */
                HAL_SPI_Transmit(&hspi1, spi_tx_buf, spi_tx_len, 100);

                /* Clear buffer */
                memset(spi_rx_buf, 0, sizeof(spi_rx_buf));
            }
        }

        /* Blink status LED based on state */
        static uint32_t last_blink = 0;
        uint32_t now = HAL_GetTick();

        if (now - last_blink >= 500) {
            last_blink = now;

            switch (g_state) {
                case STATE_IDLE:
                    set_status_led(true);  /* Solid on */
                    break;
                case STATE_UPLOADING:
                case STATE_FLASHING:
                    set_status_led(!HAL_GPIO_ReadPin(STATUS_LED_PORT, STATUS_LED_PIN));  /* Fast blink */
                    break;
                case STATE_BOOTING:
                case STATE_RUNNING:
                    /* Slow blink handled by 500ms interval */
                    set_status_led(!HAL_GPIO_ReadPin(STATUS_LED_PORT, STATUS_LED_PIN));
                    break;
                case STATE_COMPLETED:
                    set_status_led(true);  /* Solid on */
                    break;
                case STATE_ERROR:
                    set_status_led(false); /* Off or rapid blink */
                    break;
            }
        }
    }
}
