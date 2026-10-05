import { spawn, execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { readFile, writeFile, mkdir, access, unlink } from 'node:fs/promises';
import { dirname, join, resolve } from 'node:path';
import { homedir } from 'node:os';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { buildAutostart, autostartExecutable, nativeExecutable } from '../native/build.mjs';

export function shortcutConfiguration(projectRoot, nodeExecutable, startupDirectory) {
  return {
    shortcutPath:join(startupDirectory, 'Codex Usage Rings.lnk'),
    targetPath:join(projectRoot, 'native', 'build', 'CodexUsageRingsAutostart.exe'),
    arguments:'--node "' + nodeExecutable + '"',
    workingDirectory:projectRoot,
    description:'Automatically start Usage Rings when Codex Desktop runs',
  };
}

const shortcutCommand = `
$ErrorActionPreference = 'Stop'
$configuration = $env:USAGE_RINGS_SHORTCUT_CONFIGURATION | ConvertFrom-Json
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($configuration.shortcutPath)
$shortcut.TargetPath = $configuration.targetPath
$shortcut.Arguments = $configuration.arguments
$shortcut.WorkingDirectory = $configuration.workingDirectory
$shortcut.Description = $configuration.description
$shortcut.Save()
`;

export function removeUsageAdapters(configuration, adapterPaths) {
  const normalizedPaths = adapterPaths.map(path => path.replaceAll(String.fromCharCode(92), '/').toLowerCase());
  const result = structuredClone(configuration);
  for (const event of ['SessionStart', 'UserPromptSubmit']) {
    const groups = result.hooks?.[event];
    if (!groups) continue;
    result.hooks[event] = groups.flatMap(group => {
      const handlers = (group.hooks || []).filter(handler => {
        const command = handler.command?.replaceAll(String.fromCharCode(92), '/').toLowerCase() || '';
        return !normalizedPaths.some(path => command.includes(path));
      });
      if (handlers.length === (group.hooks || []).length) return [group];
      return handlers.length > 0 ? [{ ...group, hooks:handlers }] : [];
    });
    if (result.hooks[event].length === 0) delete result.hooks[event];
  }
  return result;
}

async function removeOldAdapter(codexHome, projectRoot) {
  const target = join(codexHome, 'hooks.json');
  let configuration;
  try { configuration = JSON.parse(await readFile(target, 'utf8')); }
  catch (error) { if (error.code === 'ENOENT') return; throw error; }
  const result = removeUsageAdapters(configuration, [
    join(projectRoot, 'hooks', 'session-adapter.mjs'),
    join(homedir(), '.claude', 'codex-usage-rings', 'hooks', 'session-adapter.mjs'),
  ]);
  if (JSON.stringify(configuration) !== JSON.stringify(result)) {
    await writeFile(target, JSON.stringify(result, null, 2) + '\n', 'utf8');
  }
}

export async function installAutostart({ remove = false } = {}) {
  if (process.platform !== 'win32') throw new Error('The autostart supervisor currently supports Windows only.');
  const projectRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');
  const codexHome = process.env.CODEX_HOME || join(homedir(), '.codex');
  const startupDirectory = join(process.env.APPDATA, 'Microsoft', 'Windows', 'Start Menu', 'Programs', 'Startup');
  const configuration = shortcutConfiguration(projectRoot, process.execPath, startupDirectory);
  const recordPath = join(projectRoot, 'Autostart_Installation_Private.json');
  await removeOldAdapter(codexHome, projectRoot);
  if (remove) {
    await unlink(configuration.shortcutPath).catch(error => { if (error.code !== 'ENOENT') throw error; });
    try {
      await access(autostartExecutable);
      await promisify(execFile)(autostartExecutable, ['--stop'], { windowsHide:true });
    } catch (error) { if (error.code !== 'ENOENT') throw error; }
    await writeFile(recordPath, JSON.stringify({ enabled:false, ...configuration }, null, 2) + '\n', 'utf8');
    return { enabled:false, ...configuration };
  }
  await access(nativeExecutable);
  try {
    await access(autostartExecutable);
    await promisify(execFile)(autostartExecutable, ['--stop'], { windowsHide:true });
  } catch (error) { if (error.code !== 'ENOENT') throw error; }
  await buildAutostart();
  await mkdir(startupDirectory, { recursive:true });
  await promisify(execFile)('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', shortcutCommand], {
    windowsHide:true,
    env:{ ...process.env, USAGE_RINGS_SHORTCUT_CONFIGURATION:JSON.stringify(configuration) },
    encoding:'utf8',
  });
  await writeFile(recordPath, JSON.stringify({
    enabled:true, installedAt:new Date().toISOString(), ...configuration,
  }, null, 2) + '\n', 'utf8');
  const child = spawn(autostartExecutable, ['--node', process.execPath], {
    cwd:projectRoot, detached:true, stdio:'ignore', windowsHide:true,
  });
  await new Promise((resolve, reject) => { child.once('spawn', resolve); child.once('error', reject); });
  child.unref();
  return { enabled:true, ...configuration };
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  try {
    const result = await installAutostart({ remove:process.argv.includes('--remove') });
    console.log(result.enabled ? 'Installed the silent autostart supervisor. After Windows sign-in it starts the widget with the desktop app and restores it if it exits.' : 'Removed autostart and stopped the supervisor. The current widget keeps running.');
  } catch (error) {
    console.error(error.stderr || error.message);
    process.exitCode = 1;
  }
}