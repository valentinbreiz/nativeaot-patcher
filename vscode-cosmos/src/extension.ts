import * as vscode from 'vscode';
import * as path from 'path';
import * as fs from 'fs';
import * as os from 'os';
import { execSync, spawn } from 'child_process';

let outputChannel: vscode.OutputChannel;

// Get PATH with dotnet tools directory included
function getEnvWithDotnetTools(): NodeJS.ProcessEnv {
    const home = os.homedir();
    const dotnetToolsPath = path.join(home, '.dotnet', 'tools');
    const currentPath = process.env.PATH || '';
    const pathSeparator = process.platform === 'win32' ? ';' : ':';

    return {
        ...process.env,
        PATH: `${dotnetToolsPath}${pathSeparator}${currentPath}`
    };
}

function execWithPath(command: string, options: { encoding: 'utf8'; timeout?: number; cwd?: string } = { encoding: 'utf8' }): string {
    try {
        return execSync(command, {
            ...options,
            env: getEnvWithDotnetTools(),
            shell: '/bin/bash'
        });
    } catch (e) {
        throw e;
    }
}
let projectTreeProvider: ProjectTreeProvider;
let toolsTreeProvider: ToolsTreeProvider;

export function activate(context: vscode.ExtensionContext) {
    outputChannel = vscode.window.createOutputChannel('Cosmos OS');

    // Initialize tree providers
    projectTreeProvider = new ProjectTreeProvider();
    toolsTreeProvider = new ToolsTreeProvider();

    // Register tree views
    vscode.window.registerTreeDataProvider('cosmos.project', projectTreeProvider);
    vscode.window.registerTreeDataProvider('cosmos.tools', toolsTreeProvider);

    // Register commands
    context.subscriptions.push(
        vscode.commands.registerCommand('cosmos.newProject', () => newProjectCommand(context)),
        vscode.commands.registerCommand('cosmos.checkTools', checkToolsCommand),
        vscode.commands.registerCommand('cosmos.installTools', installToolsCommand),
        vscode.commands.registerCommand('cosmos.build', buildCommand),
        vscode.commands.registerCommand('cosmos.run', runCommand),
        vscode.commands.registerCommand('cosmos.debug', debugCommand),
        vscode.commands.registerCommand('cosmos.clean', cleanCommand),
        vscode.commands.registerCommand('cosmos.refreshTools', () => toolsTreeProvider.refresh()),
        vscode.commands.registerCommand('cosmos.projectProperties', () => showProjectProperties(context))
    );

    // Check if this is a Cosmos project and update context
    updateCosmosProjectContext();

    // Watch for workspace changes
    context.subscriptions.push(
        vscode.workspace.onDidChangeWorkspaceFolders(() => {
            updateCosmosProjectContext();
            projectTreeProvider.refresh();
            toolsTreeProvider.refresh();
        })
    );
}

export function deactivate() { }

function findCsprojFiles(dir: string, depth: number = 0): string[] {
    if (depth > 3) return []; // Limit search depth
    const results: string[] = [];

    try {
        const entries = fs.readdirSync(dir, { withFileTypes: true });
        for (const entry of entries) {
            const fullPath = path.join(dir, entry.name);
            if (entry.isFile() && entry.name.endsWith('.csproj')) {
                results.push(fullPath);
            } else if (entry.isDirectory() && !entry.name.startsWith('.') && entry.name !== 'node_modules' && entry.name !== 'bin' && entry.name !== 'obj') {
                results.push(...findCsprojFiles(fullPath, depth + 1));
            }
        }
    } catch { }

    return results;
}

function isCosmosProject(): boolean {
    const workspaceFolders = vscode.workspace.workspaceFolders;
    if (!workspaceFolders) return false;

    for (const folder of workspaceFolders) {
        const csprojFiles = findCsprojFiles(folder.uri.fsPath);

        for (const csproj of csprojFiles) {
            try {
                const content = fs.readFileSync(csproj, 'utf8');
                if (content.includes('Cosmos.Sdk') || content.includes('Cosmos.Kernel')) {
                    return true;
                }
            } catch { }
        }
    }
    return false;
}

function updateCosmosProjectContext() {
    const isCosmos = isCosmosProject();
    vscode.commands.executeCommand('setContext', 'cosmos:isCosmosProject', isCosmos);
}

function getProjectInfo(): { name: string; arch: string; csproj: string } | null {
    const workspaceFolders = vscode.workspace.workspaceFolders;
    if (!workspaceFolders) return null;

    for (const folder of workspaceFolders) {
        const csprojFiles = findCsprojFiles(folder.uri.fsPath);

        for (const csproj of csprojFiles) {
            try {
                const content = fs.readFileSync(csproj, 'utf8');
                if (content.includes('Cosmos.Sdk') || content.includes('Cosmos.Kernel')) {
                    const archMatch = content.match(/<CosmosArch>(\w+)<\/CosmosArch>/);
                    return {
                        name: path.basename(csproj, '.csproj'),
                        arch: archMatch ? archMatch[1] : 'x64',
                        csproj: csproj
                    };
                }
            } catch { }
        }
    }
    return null;
}

// ============================================================================
// Tree Providers
// ============================================================================

class ProjectTreeProvider implements vscode.TreeDataProvider<ProjectItem> {
    private _onDidChangeTreeData = new vscode.EventEmitter<ProjectItem | undefined>();
    readonly onDidChangeTreeData = this._onDidChangeTreeData.event;

    refresh(): void {
        this._onDidChangeTreeData.fire(undefined);
    }

    getTreeItem(element: ProjectItem): vscode.TreeItem {
        return element;
    }

    getChildren(element?: ProjectItem): ProjectItem[] {
        if (element) return [];

        const project = getProjectInfo();
        if (!project) return [];

        const arch = project.arch;
        const archLabel = arch === 'arm64' ? 'ARM64' : 'x64';
        const archDesc = arch === 'arm64' ? 'ARM 64-bit' : 'Intel/AMD 64-bit';

        return [
            new ProjectItem('Properties', 'Edit project settings', 'cosmos.projectProperties', undefined, '$(settings-gear)'),
            new ProjectItem(`Build`, `Build for ${archDesc}`, 'cosmos.build', arch, '$(gear)'),
            new ProjectItem(`Run`, `Run in QEMU (${archLabel})`, 'cosmos.run', arch, '$(play)'),
            new ProjectItem(`Debug`, `Debug with GDB (${archLabel})`, 'cosmos.debug', arch, '$(debug-alt)'),
            new ProjectItem('Clean', 'Remove build outputs', 'cosmos.clean', undefined, '$(trash)')
        ];
    }
}

class ProjectItem extends vscode.TreeItem {
    constructor(
        label: string,
        tooltip: string,
        commandId: string,
        public readonly arch?: string,
        icon?: string
    ) {
        super(label, vscode.TreeItemCollapsibleState.None);
        this.tooltip = tooltip;
        this.iconPath = icon ? new vscode.ThemeIcon(icon.replace('$(', '').replace(')', '')) : undefined;
        this.command = {
            command: commandId,
            title: label,
            arguments: arch ? [arch] : []
        };
    }
}

class ToolsTreeProvider implements vscode.TreeDataProvider<ToolItem> {
    private _onDidChangeTreeData = new vscode.EventEmitter<ToolItem | undefined>();
    readonly onDidChangeTreeData = this._onDidChangeTreeData.event;
    private tools: ToolItem[] = [];

    refresh(): void {
        this.checkTools();
        this._onDidChangeTreeData.fire(undefined);
    }

    getTreeItem(element: ToolItem): vscode.TreeItem {
        return element;
    }

    getChildren(element?: ToolItem): ToolItem[] {
        if (element) return [];

        if (this.tools.length === 0) {
            this.checkTools();
        }
        return this.tools;
    }

    private checkTools() {
        this.tools = [];

        // Check dotnet
        this.tools.push(this.checkCommand('dotnet', 'dotnet --version', '.NET SDK'));

        // Check cosmos-tools (check file directly)
        const home = os.homedir();
        const cosmosToolsPath = path.join(home, '.dotnet', 'tools', 'cosmos-tools');
        if (fs.existsSync(cosmosToolsPath)) {
            this.tools.push(new ToolItem('Cosmos Tools', true, 'Installed'));
        } else {
            this.tools.push(new ToolItem('Cosmos Tools', false, 'Not installed'));
        }

        // Check QEMU x64
        this.tools.push(this.checkCommand('qemu-system-x86_64', 'qemu-system-x86_64 --version', 'QEMU x64'));

        // Check QEMU ARM64
        this.tools.push(this.checkCommand('qemu-system-aarch64', 'qemu-system-aarch64 --version', 'QEMU ARM64'));

        // Check GDB
        this.tools.push(this.checkCommand('gdb', 'gdb --version', 'GDB Debugger'));
    }

    private checkCommand(name: string, command: string, displayName: string): ToolItem {
        try {
            const output = execWithPath(command, { encoding: 'utf8', timeout: 5000 }).split('\n')[0];
            return new ToolItem(displayName, true, output.trim());
        } catch {
            return new ToolItem(displayName, false, 'Not installed');
        }
    }
}

class ToolItem extends vscode.TreeItem {
    constructor(label: string, installed: boolean, version: string) {
        super(label, vscode.TreeItemCollapsibleState.None);
        this.description = version;
        this.iconPath = new vscode.ThemeIcon(
            installed ? 'check' : 'x',
            installed ? new vscode.ThemeColor('testing.iconPassed') : new vscode.ThemeColor('testing.iconFailed')
        );
        this.tooltip = installed ? `${label}: ${version}` : `${label} is not installed`;
    }
}

// ============================================================================
// Commands
// ============================================================================

async function newProjectCommand(context: vscode.ExtensionContext) {
    // Check if cosmos-tools is installed (check file directly)
    const home = os.homedir();
    const cosmosToolsPath = path.join(home, '.dotnet', 'tools', 'cosmos-tools');
    const cosmosToolsInstalled = fs.existsSync(cosmosToolsPath);

    if (!cosmosToolsInstalled) {
        const install = await vscode.window.showWarningMessage(
            'Cosmos Tools is required to create projects. Install now?',
            'Install', 'Cancel'
        );
        if (install !== 'Install') return;

        const terminal = vscode.window.createTerminal('Cosmos Setup');
        terminal.show();
        terminal.sendText('dotnet tool install -g Cosmos.Tools && cosmos-tools install');

        await vscode.window.showInformationMessage(
            'Installing Cosmos Tools. Please run "Create Kernel Project" again after installation completes.',
            'OK'
        );
        return;
    }

    // Check if templates are installed
    let templatesInstalled = false;
    try {
        const result = execWithPath('dotnet new list cosmos-kernel', { encoding: 'utf8' });
        templatesInstalled = result.includes('cosmos-kernel');
    } catch { }

    if (!templatesInstalled) {
        const terminal = vscode.window.createTerminal('Cosmos Setup');
        terminal.show();
        terminal.sendText('dotnet new install Cosmos.Build.Templates');

        await vscode.window.showInformationMessage(
            'Installing Cosmos templates. Please run "Create Kernel Project" again after installation completes.',
            'OK'
        );
        return;
    }

    // Ask for project name
    const projectName = await vscode.window.showInputBox({
        prompt: 'Enter the kernel project name',
        placeHolder: 'MyKernel',
        validateInput: (value) => {
            if (!value) return 'Project name is required';
            if (!/^[a-zA-Z][a-zA-Z0-9_]*$/.test(value)) {
                return 'Project name must start with a letter and contain only letters, numbers, and underscores';
            }
            return null;
        }
    });

    if (!projectName) return;

    // Ask for target architecture
    const arch = await vscode.window.showQuickPick(
        [
            { label: 'x64', description: 'Intel/AMD 64-bit (recommended for most users)' },
            { label: 'arm64', description: 'ARM 64-bit (Raspberry Pi, Apple Silicon VMs)' }
        ],
        {
            placeHolder: 'Select target architecture',
            title: 'Target Architecture'
        }
    );

    if (!arch) return;

    // Ask where to create the project
    const location = await vscode.window.showQuickPick(
        [
            { label: 'Current Folder', description: 'Create project files here', value: 'current' },
            { label: 'New Folder', description: 'Create in a new subfolder', value: 'subfolder' },
            { label: 'Choose Location...', description: 'Select a different location', value: 'browse' }
        ],
        {
            placeHolder: 'Where should the project be created?',
            title: 'Project Location'
        }
    );

    if (!location) return;

    let projectPath: string;
    let createInCurrentDir = false;
    const workspaceFolder = vscode.workspace.workspaceFolders?.[0];

    if (location.value === 'current' && workspaceFolder) {
        projectPath = workspaceFolder.uri.fsPath;
        createInCurrentDir = true;
    } else if (location.value === 'subfolder' && workspaceFolder) {
        projectPath = path.join(workspaceFolder.uri.fsPath, projectName);
    } else {
        const folderUri = await vscode.window.showOpenDialog({
            canSelectFiles: false,
            canSelectFolders: true,
            canSelectMany: false,
            openLabel: 'Select Location',
            title: 'Select project location'
        });

        if (!folderUri || folderUri.length === 0) return;
        projectPath = path.join(folderUri[0].fsPath, projectName);
    }

    // Create the project
    outputChannel.show();
    outputChannel.appendLine(`Creating Cosmos kernel project: ${projectName}`);
    outputChannel.appendLine(`Architecture: ${arch.label}`);
    outputChannel.appendLine(`Location: ${projectPath}`);
    outputChannel.appendLine('');

    try {
        // Create directory if needed
        if (!fs.existsSync(projectPath)) {
            fs.mkdirSync(projectPath, { recursive: true });
        }

        // Run dotnet new (use -o . when creating in current dir to avoid subdirectory)
        const outputFlag = createInCurrentDir ? '-o .' : '';
        const cmd = `dotnet new cosmos-kernel -n ${projectName} --TargetArch ${arch.label} ${outputFlag} --force`;
        outputChannel.appendLine(`> ${cmd}`);

        const result = execWithPath(cmd, {
            cwd: projectPath,
            encoding: 'utf8'
        });
        outputChannel.appendLine(result);
        outputChannel.appendLine('');
        outputChannel.appendLine('Project created successfully!');

        // Open the project automatically
        if (createInCurrentDir) {
            // Just refresh the context since we're already in the right folder
            updateCosmosProjectContext();
            projectTreeProvider.refresh();
            toolsTreeProvider.refresh();
            vscode.window.showInformationMessage(`Cosmos kernel "${projectName}" created successfully!`);
        } else {
            // Open the new project folder
            await vscode.commands.executeCommand('vscode.openFolder', vscode.Uri.file(projectPath), false);
        }
    } catch (error: any) {
        outputChannel.appendLine(`Error: ${error.message}`);
        if (error.stdout) outputChannel.appendLine(error.stdout);
        if (error.stderr) outputChannel.appendLine(error.stderr);
        vscode.window.showErrorMessage(`Failed to create project: ${error.message}`);
    }
}

async function checkToolsCommand() {
    toolsTreeProvider.refresh();

    outputChannel.show();
    outputChannel.appendLine('Checking development tools...');
    outputChannel.appendLine('');

    try {
        const result = execWithPath('cosmos-tools check', { encoding: 'utf8' });
        outputChannel.appendLine(result);
    } catch (error: any) {
        if (error.stdout) {
            outputChannel.appendLine(error.stdout);
        }
        vscode.window.showWarningMessage(
            'Some development tools are missing. Run "Install Tools" to install them.',
            'Install Tools'
        ).then(selection => {
            if (selection === 'Install Tools') {
                installToolsCommand();
            }
        });
    }
}

async function installToolsCommand() {
    const terminal = vscode.window.createTerminal('Cosmos Tools');
    terminal.show();
    terminal.sendText('cosmos-tools install');
}

async function buildCommand(arch?: string) {
    if (!arch) {
        const selected = await vscode.window.showQuickPick(
            [
                { label: 'x64', description: 'Intel/AMD 64-bit' },
                { label: 'arm64', description: 'ARM 64-bit' }
            ],
            { placeHolder: 'Select target architecture' }
        );
        if (!selected) return;
        arch = selected.label;
    }

    const config = await vscode.window.showQuickPick(
        [
            { label: 'Debug', description: 'Debug build with symbols' },
            { label: 'Release', description: 'Optimized release build' }
        ],
        { placeHolder: 'Select build configuration' }
    );

    if (!config) return;

    const projectInfo = getProjectInfo();
    if (!projectInfo) {
        vscode.window.showErrorMessage('No Cosmos project found');
        return;
    }

    const projectDir = path.dirname(projectInfo.csproj);
    const archUpper = arch.toUpperCase();
    const terminal = vscode.window.createTerminal('Cosmos Build');
    terminal.show();
    terminal.sendText(
        `cd "${projectDir}" && dotnet publish -c ${config.label} -r linux-${arch} ` +
        `-p:DefineConstants=ARCH_${archUpper} -p:CosmosArch=${arch} ` +
        `-o ./output-${arch} --verbosity minimal`
    );
}

async function runCommand(arch?: string) {
    if (!arch) {
        const selected = await vscode.window.showQuickPick(
            [
                { label: 'x64', description: 'Run x64 kernel in QEMU' },
                { label: 'arm64', description: 'Run ARM64 kernel in QEMU' }
            ],
            { placeHolder: 'Select architecture to run' }
        );
        if (!selected) return;
        arch = selected.label;
    }

    const projectInfo = getProjectInfo();
    if (!projectInfo) {
        vscode.window.showErrorMessage('No Cosmos project found');
        return;
    }

    const projectDir = path.dirname(projectInfo.csproj);
    const outputDir = path.join(projectDir, `output-${arch}`);

    if (!fs.existsSync(outputDir)) {
        const build = await vscode.window.showWarningMessage(
            `No build found for ${arch}. Build first?`,
            'Build', 'Cancel'
        );
        if (build === 'Build') {
            await buildCommand(arch);
        }
        return;
    }

    const isoFiles = fs.readdirSync(outputDir).filter(f => f.endsWith('.iso'));
    if (isoFiles.length === 0) {
        vscode.window.showErrorMessage('No ISO file found. Please build the project first.');
        return;
    }

    const isoPath = path.join(outputDir, isoFiles[0]);
    const config = vscode.workspace.getConfiguration('cosmos');
    const memory = config.get<string>('qemuMemory') || '512M';

    const terminal = vscode.window.createTerminal('QEMU');
    terminal.show();

    if (arch === 'x64') {
        terminal.sendText(
            `qemu-system-x86_64 -M q35 -cpu max -m ${memory} -serial stdio ` +
            `-cdrom "${isoPath}" -display gtk -vga std -no-reboot -no-shutdown`
        );
    } else {
        terminal.sendText(
            `qemu-system-aarch64 -M virt -cpu cortex-a72 -m 1G ` +
            `-bios /usr/share/AAVMF/AAVMF_CODE.fd ` +
            `-drive if=none,id=cd,file="${isoPath}" ` +
            `-device virtio-scsi-pci -device scsi-cd,drive=cd,bootindex=0 ` +
            `-device virtio-keyboard-device -device ramfb ` +
            `-display gtk,show-cursor=on -serial stdio`
        );
    }
}

async function debugCommand(arch?: string) {
    if (!arch) {
        const selected = await vscode.window.showQuickPick(
            [
                { label: 'x64', description: 'Debug x64 kernel' },
                { label: 'arm64', description: 'Debug ARM64 kernel' }
            ],
            { placeHolder: 'Select architecture to debug' }
        );
        if (!selected) return;
        arch = selected.label;
    }

    const workspaceFolder = vscode.workspace.workspaceFolders?.[0];
    if (!workspaceFolder) {
        vscode.window.showErrorMessage('No workspace folder open');
        return;
    }

    const projectInfo = getProjectInfo();
    if (!projectInfo) {
        vscode.window.showErrorMessage('No Cosmos project found');
        return;
    }

    const projectDir = path.dirname(projectInfo.csproj);
    const outputDir = path.join(projectDir, `output-${arch}`);
    const binDir = path.join(projectDir, 'bin', 'Debug', 'net10.0', `linux-${arch}`);

    // Check if build exists (ISO in output dir)
    if (!fs.existsSync(outputDir)) {
        const build = await vscode.window.showWarningMessage(
            `No build found for ${arch}. Build first?`,
            'Build', 'Cancel'
        );
        if (build === 'Build') {
            await buildCommand(arch);
        }
        return;
    }

    // Find ISO in output dir
    const outputFiles = fs.readdirSync(outputDir);
    const isoFiles = outputFiles.filter(f => f.endsWith('.iso'));

    // Find ELF in bin dir
    let elfPath: string | null = null;
    if (fs.existsSync(binDir)) {
        const binFiles = fs.readdirSync(binDir);
        const elfFiles = binFiles.filter(f => f.endsWith('.elf'));
        if (elfFiles.length > 0) {
            elfPath = path.join(binDir, elfFiles[0]);
        }
    }

    if (isoFiles.length === 0) {
        const build = await vscode.window.showWarningMessage(
            `Build incomplete for ${arch} (missing ISO). Rebuild?`,
            'Build', 'Cancel'
        );
        if (build === 'Build') {
            await buildCommand(arch);
        }
        return;
    }

    if (!elfPath) {
        const build = await vscode.window.showWarningMessage(
            `Build incomplete for ${arch} (missing ELF for debugging). Rebuild?`,
            'Build', 'Cancel'
        );
        if (build === 'Build') {
            await buildCommand(arch);
        }
        return;
    }

    const isoPath = path.join(outputDir, isoFiles[0]);

    // Start QEMU with debug flags in a terminal
    const qemuTerminal = vscode.window.createTerminal('QEMU Debug');
    qemuTerminal.show();

    if (arch === 'x64') {
        qemuTerminal.sendText(
            `qemu-system-x86_64 -M q35 -cpu max -m 512M -serial stdio ` +
            `-cdrom "${isoPath}" -display gtk -vga std -s -S -no-reboot -no-shutdown`
        );
    } else {
        qemuTerminal.sendText(
            `qemu-system-aarch64 -M virt -cpu cortex-a72 -m 1G ` +
            `-bios /usr/share/AAVMF/AAVMF_CODE.fd ` +
            `-drive if=none,id=cd,file="${isoPath}" ` +
            `-device virtio-scsi-pci -device scsi-cd,drive=cd,bootindex=0 ` +
            `-device virtio-keyboard-device -device ramfb ` +
            `-display gtk,show-cursor=on -serial stdio -s -S`
        );
    }

    // Wait for QEMU to start
    await new Promise(resolve => setTimeout(resolve, 2000));

    // Create debug configuration
    const debugConfig: vscode.DebugConfiguration = {
        name: `Debug ${arch} Kernel`,
        type: 'cppdbg',
        request: 'launch',
        MIMode: 'gdb',
        miDebuggerPath: arch === 'x64' ? 'gdb' : 'gdb-multiarch',
        miDebuggerServerAddress: 'localhost:1234',
        program: elfPath,
        cwd: projectDir,
        stopAtEntry: false,
        setupCommands: [
            { text: '-enable-pretty-printing', ignoreFailures: true },
            { text: 'set pagination off', ignoreFailures: true }
        ]
    };

    if (arch === 'arm64') {
        debugConfig.setupCommands?.push(
            { text: 'set architecture aarch64', ignoreFailures: false },
            { text: 'set endian little', ignoreFailures: true }
        );
    }

    // Start debugging
    vscode.debug.startDebugging(workspaceFolder, debugConfig);
}

async function cleanCommand() {
    const projectInfo = getProjectInfo();
    if (!projectInfo) {
        vscode.window.showErrorMessage('No Cosmos project found');
        return;
    }

    const confirm = await vscode.window.showWarningMessage(
        'Delete all build outputs?',
        'Yes', 'No'
    );

    if (confirm !== 'Yes') return;

    const projectDir = path.dirname(projectInfo.csproj);
    const dirsToClean = ['output-x64', 'output-arm64', 'bin', 'obj'];
    let cleaned = 0;

    for (const dir of dirsToClean) {
        const dirPath = path.join(projectDir, dir);
        if (fs.existsSync(dirPath)) {
            fs.rmSync(dirPath, { recursive: true, force: true });
            cleaned++;
        }
    }

    vscode.window.showInformationMessage(`Cleaned ${cleaned} directories`);
}

// ============================================================================
// Project Properties Panel
// ============================================================================

interface ProjectProperties {
    name: string;
    targetFramework: string;
    targetArch: string;
    kernelClass: string;
    enableGraphics: boolean;
    gccFlags: string;
    defaultFont: string;
    packages: { name: string; version: string }[];
}

function parseProjectProperties(csprojPath: string): ProjectProperties {
    const content = fs.readFileSync(csprojPath, 'utf8');
    const name = path.basename(csprojPath, '.csproj');

    // Parse properties using regex
    const getProperty = (prop: string): string => {
        const match = content.match(new RegExp(`<${prop}>([^<]*)</${prop}>`));
        return match ? match[1] : '';
    };

    // Check for package references
    const hasPackage = (pkg: string): boolean => {
        return content.includes(`Include="${pkg}"`);
    };

    // Get all package references
    const packageMatches = content.matchAll(/<PackageReference\s+Include="([^"]+)"\s+Version="([^"]+)"/g);
    const packages: { name: string; version: string }[] = [];
    for (const match of packageMatches) {
        if (!match[1].startsWith('Cosmos.Build.') && !match[1].startsWith('Cosmos.Kernel.Native')) {
            packages.push({ name: match[1], version: match[2] });
        }
    }

    return {
        name,
        targetFramework: getProperty('TargetFramework') || 'net10.0',
        targetArch: getProperty('CosmosArch') || 'x64',
        kernelClass: getProperty('CosmosKernelClass') || `${name}.Kernel`,
        enableGraphics: hasPackage('Cosmos.Kernel.Graphics'),
        gccFlags: getProperty('GCCCompilerFlags') || '',
        defaultFont: getProperty('CosmosDefaultFont') || '',
        packages
    };
}

function saveProjectProperties(csprojPath: string, props: ProjectProperties): void {
    let content = fs.readFileSync(csprojPath, 'utf8');

    // Helper to set or add property
    const setProperty = (prop: string, value: string) => {
        const regex = new RegExp(`<${prop}>[^<]*</${prop}>`);
        if (regex.test(content)) {
            content = content.replace(regex, `<${prop}>${value}</${prop}>`);
        } else {
            // Add to first PropertyGroup (handle both <PropertyGroup> and <PropertyGroup ...>)
            const pgMatch = content.match(/<PropertyGroup[^>]*>/);
            if (pgMatch) {
                content = content.replace(
                    pgMatch[0],
                    `${pgMatch[0]}\n    <${prop}>${value}</${prop}>`
                );
            }
        }
    };

    // Helper to remove property if empty
    const removeProperty = (prop: string) => {
        content = content.replace(new RegExp(`\\s*<${prop}>[^<]*</${prop}>`, 'g'), '');
    };

    // Update properties (always update, even if empty to remove them)
    setProperty('TargetFramework', props.targetFramework || 'net10.0');
    setProperty('CosmosArch', props.targetArch || 'x64');

    if (props.kernelClass) {
        setProperty('CosmosKernelClass', props.kernelClass);
    }

    if (props.gccFlags) {
        setProperty('GCCCompilerFlags', props.gccFlags);
    } else {
        removeProperty('GCCCompilerFlags');
    }

    if (props.defaultFont) {
        setProperty('CosmosDefaultFont', props.defaultFont);
    } else {
        removeProperty('CosmosDefaultFont');
    }

    // Handle graphics package
    const graphicsRef = '<PackageReference Include="Cosmos.Kernel.Graphics"';
    if (props.enableGraphics && !content.includes(graphicsRef)) {
        // Add graphics package
        const insertPoint = content.indexOf('<PackageReference Include="Cosmos.Kernel.System"');
        if (insertPoint !== -1) {
            const lineEnd = content.indexOf('/>', insertPoint) + 2;
            const version = content.match(/Include="Cosmos\.Kernel\.System"\s+Version="([^"]+)"/)?.[1] || '3.0.7';
            content = content.slice(0, lineEnd) +
                `\n    <PackageReference Include="Cosmos.Kernel.Graphics" Version="${version}" />` +
                content.slice(lineEnd);
        }
    } else if (!props.enableGraphics && content.includes(graphicsRef)) {
        // Remove graphics package
        content = content.replace(/\s*<PackageReference Include="Cosmos\.Kernel\.Graphics"[^/]*\/>/g, '');
    }

    fs.writeFileSync(csprojPath, content);
}

function showProjectProperties(context: vscode.ExtensionContext) {
    const projectInfo = getProjectInfo();
    if (!projectInfo) {
        vscode.window.showErrorMessage('No Cosmos project found');
        return;
    }

    const props = parseProjectProperties(projectInfo.csproj);

    const panel = vscode.window.createWebviewPanel(
        'cosmosProperties',
        `${props.name} - Properties`,
        vscode.ViewColumn.One,
        {
            enableScripts: true,
            retainContextWhenHidden: true
        }
    );

    panel.webview.html = getPropertiesWebviewContent(props, projectInfo.csproj);

    panel.webview.onDidReceiveMessage(
        message => {
            switch (message.command) {
                case 'save':
                    try {
                        saveProjectProperties(projectInfo.csproj, message.properties);
                        vscode.window.showInformationMessage('Project properties saved successfully');
                        projectTreeProvider.refresh();
                    } catch (error: any) {
                        vscode.window.showErrorMessage(`Failed to save: ${error.message}`);
                    }
                    break;
                case 'openCsproj':
                    vscode.workspace.openTextDocument(projectInfo.csproj).then(doc => {
                        vscode.window.showTextDocument(doc);
                    });
                    break;
            }
        },
        undefined,
        context.subscriptions
    );
}

function getPropertiesWebviewContent(props: ProjectProperties, csprojPath: string): string {
    return `<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>Project Properties</title>
    <style>
        * {
            box-sizing: border-box;
        }
        body {
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Oxygen, Ubuntu, sans-serif;
            padding: 0;
            margin: 0;
            color: var(--vscode-foreground);
            background-color: var(--vscode-editor-background);
            line-height: 1.5;
        }
        .container {
            padding: 32px 24px;
        }
        .header {
            margin-bottom: 32px;
        }
        .header-top {
            display: flex;
            justify-content: space-between;
            align-items: flex-start;
        }
        .header-actions {
            display: flex;
            align-items: center;
            gap: 12px;
        }
        .header h1 {
            font-size: 28px;
            font-weight: 600;
            margin: 0 0 4px 0;
            letter-spacing: -0.5px;
        }
        .header .subtitle {
            color: var(--vscode-descriptionForeground);
            font-size: 14px;
        }
        .section {
            margin-bottom: 32px;
        }
        .section-title {
            font-size: 11px;
            font-weight: 600;
            text-transform: uppercase;
            letter-spacing: 0.5px;
            color: var(--vscode-descriptionForeground);
            margin-bottom: 16px;
            padding-bottom: 8px;
            border-bottom: 1px solid var(--vscode-widget-border, rgba(128,128,128,0.2));
        }
        .field {
            margin-bottom: 20px;
        }
        .field:last-child {
            margin-bottom: 0;
        }
        .field-label {
            font-size: 13px;
            font-weight: 500;
            margin-bottom: 6px;
            display: block;
        }
        .field-input {
            width: 100%;
            padding: 10px 12px;
            font-size: 14px;
            border: 1px solid var(--vscode-input-border, rgba(128,128,128,0.3));
            background-color: var(--vscode-input-background);
            color: var(--vscode-input-foreground);
            border-radius: 6px;
            transition: border-color 0.15s, box-shadow 0.15s;
        }
        .field-input:focus {
            outline: none;
            border-color: var(--vscode-focusBorder);
            box-shadow: 0 0 0 3px rgba(var(--vscode-focusBorder), 0.1);
        }
        .field-input[readonly] {
            opacity: 0.6;
            cursor: not-allowed;
        }
        .field-hint {
            font-size: 12px;
            color: var(--vscode-descriptionForeground);
            margin-top: 6px;
        }
        .toggle-field {
            display: flex;
            align-items: center;
            justify-content: space-between;
            padding: 12px 0;
            border-bottom: 1px solid var(--vscode-widget-border, rgba(128,128,128,0.1));
        }
        .toggle-field:last-child {
            border-bottom: none;
        }
        .toggle-info {
            flex: 1;
        }
        .toggle-label {
            font-size: 14px;
            font-weight: 500;
            margin-bottom: 2px;
        }
        .toggle-hint {
            font-size: 12px;
            color: var(--vscode-descriptionForeground);
        }
        .toggle-switch {
            position: relative;
            width: 44px;
            height: 24px;
            margin-left: 16px;
        }
        .toggle-switch input {
            opacity: 0;
            width: 0;
            height: 0;
        }
        .toggle-slider {
            position: absolute;
            cursor: pointer;
            top: 0;
            left: 0;
            right: 0;
            bottom: 0;
            background-color: var(--vscode-input-border, #555);
            transition: 0.2s;
            border-radius: 24px;
        }
        .toggle-slider:before {
            position: absolute;
            content: "";
            height: 18px;
            width: 18px;
            left: 3px;
            bottom: 3px;
            background-color: white;
            transition: 0.2s;
            border-radius: 50%;
        }
        input:checked + .toggle-slider {
            background-color: var(--vscode-button-background, #0e639c);
        }
        input:checked + .toggle-slider:before {
            transform: translateX(20px);
        }
        .packages {
            display: flex;
            flex-direction: column;
            gap: 8px;
        }
        .package {
            display: flex;
            align-items: center;
            justify-content: space-between;
            padding: 10px 14px;
            background: var(--vscode-input-background);
            border: 1px solid var(--vscode-widget-border, rgba(128,128,128,0.2));
            border-radius: 6px;
        }
        .package-name {
            font-size: 13px;
            font-weight: 500;
        }
        .package-version {
            font-size: 12px;
            color: var(--vscode-descriptionForeground);
            background: var(--vscode-badge-background, rgba(128,128,128,0.2));
            padding: 2px 8px;
            border-radius: 10px;
        }
        .empty-packages {
            color: var(--vscode-descriptionForeground);
            font-size: 13px;
            font-style: italic;
        }
        .btn {
            display: inline-flex;
            align-items: center;
            gap: 6px;
            padding: 10px 16px;
            font-size: 13px;
            font-weight: 500;
            border: none;
            border-radius: 6px;
            cursor: pointer;
            transition: background-color 0.15s;
        }
        .btn-secondary {
            background-color: var(--vscode-button-secondaryBackground);
            color: var(--vscode-button-secondaryForeground);
        }
        .btn-secondary:hover {
            background-color: var(--vscode-button-secondaryHoverBackground);
        }
        .save-status {
            font-size: 13px;
            color: #3fb950;
            opacity: 0;
            transition: opacity 0.2s;
        }
    </style>
</head>
<body>
    <div class="container">
        <div class="header">
            <div class="header-top">
                <div>
                    <h1>${props.name}</h1>
                    <div class="subtitle">Cosmos Kernel Project</div>
                </div>
                <div class="header-actions">
                    <span id="saveStatus" class="save-status">Saved</span>
                    <button class="btn btn-secondary" onclick="openCsproj()">Edit .csproj</button>
                </div>
            </div>
        </div>

        <div class="section">
            <div class="section-title">General</div>

            <div class="field">
                <label class="field-label">.NET Version</label>
                <select id="targetFramework" class="field-input">
                    <option value="net10.0" ${props.targetFramework === 'net10.0' ? 'selected' : ''}>.NET 10</option>
                </select>
            </div>

            <div class="field">
                <label class="field-label">Target Architecture</label>
                <select id="targetArch" class="field-input">
                    <option value="x64" ${props.targetArch === 'x64' ? 'selected' : ''}>x64 (Intel/AMD 64-bit)</option>
                    <option value="arm64" ${props.targetArch === 'arm64' ? 'selected' : ''}>ARM64</option>
                </select>
            </div>

            <div class="field">
                <label class="field-label">Kernel Entry Class</label>
                <input type="text" id="kernelClass" class="field-input" value="${props.kernelClass}">
                <div class="field-hint">Fully qualified class name (e.g., MyKernel.Kernel)</div>
            </div>
        </div>

        <div class="section">
            <div class="section-title">Features</div>

            <div class="toggle-field">
                <div class="toggle-info">
                    <div class="toggle-label">Graphics Support</div>
                    <div class="toggle-hint">Framebuffer and font rendering</div>
                </div>
                <label class="toggle-switch">
                    <input type="checkbox" id="enableGraphics" ${props.enableGraphics ? 'checked' : ''}>
                    <span class="toggle-slider"></span>
                </label>
            </div>

            <div class="field" style="margin-top: 16px;">
                <label class="field-label">Default Font</label>
                <input type="text" id="defaultFont" class="field-input" value="${props.defaultFont}" placeholder="Cosmos.Kernel.Graphics.Fonts.DefaultFont.psf">
            </div>
        </div>

        <div class="section">
            <div class="section-title">Advanced</div>

            <div class="field">
                <label class="field-label">GCC Compiler Flags</label>
                <input type="text" id="gccFlags" class="field-input" value="${props.gccFlags}" placeholder="Uses SDK defaults if empty">
            </div>
        </div>

        <div class="section">
            <div class="section-title">Packages</div>

            <div class="packages">
                ${props.packages.length > 0 ? props.packages.map(p => `
                    <div class="package">
                        <span class="package-name">${p.name}</span>
                        <span class="package-version">${p.version}</span>
                    </div>
                `).join('') : '<div class="empty-packages">No additional packages</div>'}
            </div>
        </div>

    </div>

    <script>
        const vscode = acquireVsCodeApi();
        let saveTimeout;

        function save() {
            const properties = {
                targetFramework: document.getElementById('targetFramework').value,
                targetArch: document.getElementById('targetArch').value,
                kernelClass: document.getElementById('kernelClass').value,
                enableGraphics: document.getElementById('enableGraphics').checked,
                gccFlags: document.getElementById('gccFlags').value,
                defaultFont: document.getElementById('defaultFont').value
            };
            vscode.postMessage({ command: 'save', properties });
            showSaveStatus('Saved');
        }

        function showSaveStatus(text) {
            const status = document.getElementById('saveStatus');
            status.textContent = text;
            status.style.opacity = 1;
            setTimeout(() => { status.style.opacity = 0; }, 2000);
        }

        function onInputChange() {
            clearTimeout(saveTimeout);
            saveTimeout = setTimeout(save, 300);
        }

        // Auto-save on any input change
        document.getElementById('targetFramework').addEventListener('change', save);
        document.getElementById('targetArch').addEventListener('change', save);
        document.getElementById('kernelClass').addEventListener('input', onInputChange);
        document.getElementById('enableGraphics').addEventListener('change', save);
        document.getElementById('gccFlags').addEventListener('input', onInputChange);
        document.getElementById('defaultFont').addEventListener('input', onInputChange);

        function openCsproj() {
            vscode.postMessage({ command: 'openCsproj' });
        }
    </script>
</body>
</html>`;
}
