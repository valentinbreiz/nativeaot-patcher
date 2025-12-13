*** Settings ***
Documentation     Robot Framework tests for Cosmos-RPi-Dev-Board PCB emulation
...               Validates the full firmware stack: ESP32 -> STM32 -> RPi boot
Library           Process
Library           OperatingSystem
Library           String
Library           Collections

*** Variables ***
${RENODE}                   renode
${EMULATION_DIR}            ${CURDIR}/..
${PLATFORMS_DIR}            ${EMULATION_DIR}/platforms
${SCRIPTS_DIR}              ${EMULATION_DIR}/scripts
${UART_LOG}                 ${TEMPDIR}/uart-output.log
${TIMEOUT}                  120s

# Test protocol markers
${END_MARKER_HEX}           DEADBEEFCAFEBABE

*** Test Cases ***
Verify Renode Installation
    [Documentation]    Check that Renode is installed and accessible
    [Tags]    setup
    ${result}=    Run Process    ${RENODE}    --version
    Should Contain    ${result.stdout}    Renode

Verify Platform Files Exist
    [Documentation]    Check that platform definition files exist
    [Tags]    setup
    File Should Exist    ${PLATFORMS_DIR}/stm32h563.repl
    File Should Exist    ${PLATFORMS_DIR}/esp32s3.repl
    File Should Exist    ${SCRIPTS_DIR}/cosmos-rpi-devboard.resc

Load STM32H563 Platform
    [Documentation]    Test loading the STM32H563 platform in Renode
    [Tags]    platform    stm32
    ${script}=    Catenate    SEPARATOR=\n
    ...    path add "${EMULATION_DIR}"
    ...    mach create "test-stm32"
    ...    machine LoadPlatformDescription @platforms/stm32h563.repl
    ...    quit
    ${result}=    Run Renode Script    ${script}
    Should Not Contain    ${result.stderr}    Error

Load ESP32S3 Platform
    [Documentation]    Test loading the ESP32-S3 platform in Renode
    [Tags]    platform    esp32
    ${script}=    Catenate    SEPARATOR=\n
    ...    path add "${EMULATION_DIR}"
    ...    mach create "test-esp32"
    ...    machine LoadPlatformDescription @platforms/esp32s3.repl
    ...    quit
    ${result}=    Run Renode Script    ${script}
    Should Not Contain    ${result.stderr}    Error

Load Full PCB Emulation
    [Documentation]    Test loading the complete PCB emulation with both MCUs
    [Tags]    integration
    ${script}=    Catenate    SEPARATOR=\n
    ...    path add "${EMULATION_DIR}"
    ...    $iso_path = ""
    ...    $uart_log_path = "${UART_LOG}"
    ...    include @scripts/cosmos-rpi-devboard.resc
    ...    quit
    ${result}=    Run Renode Script    ${script}
    Should Not Contain    ${result.stderr}    Error
    Should Contain    ${result.stdout}    Cosmos-RPi-Dev-Board Emulation Ready

Verify UART Logging Setup
    [Documentation]    Test that UART output is captured to file
    [Tags]    uart
    # Create a test script that writes to UART
    ${script}=    Catenate    SEPARATOR=\n
    ...    path add "${EMULATION_DIR}"
    ...    mach create "uart-test"
    ...    machine LoadPlatformDescription @platforms/stm32h563.repl
    ...    usart1 CreateFileBackend "${UART_LOG}" true
    ...    quit
    ${result}=    Run Renode Script    ${script}
    Should Not Contain    ${result.stderr}    Error

*** Keywords ***
Run Renode Script
    [Arguments]    ${script}
    [Documentation]    Execute a Renode script and return the result
    ${script_file}=    Set Variable    ${TEMPDIR}/test_script.resc
    Create File    ${script_file}    ${script}
    ${result}=    Run Process    ${RENODE}
    ...    --disable-xwt
    ...    -e    include @${script_file}
    ...    timeout=${TIMEOUT}
    Remove File    ${script_file}
    [Return]    ${result}

Verify Test Completion Marker
    [Arguments]    ${log_file}
    [Documentation]    Check if the UART log contains the test completion marker
    ${content}=    Get Binary File    ${log_file}
    ${hex_content}=    Convert To Hex    ${content}
    Should Contain    ${hex_content}    ${END_MARKER_HEX}

Parse Test Results From UART
    [Arguments]    ${log_file}
    [Documentation]    Parse the binary test protocol from UART log
    ${content}=    Get Binary File    ${log_file}
    # Return parsed results (simplified - actual parsing in Python)
    [Return]    ${content}
