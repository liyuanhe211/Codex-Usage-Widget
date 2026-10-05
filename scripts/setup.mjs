import { execFile, spawn } from 'node:child_process';
import { promisify, parseArgs } from 'node:util';
import { access, readdir, readFile, writeFile, copyFile, unlink } from 'node:fs/promises';
import { dirname, join, resolve, isAbsolute } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { buildNative, buildAutostart, nativeExecutable, autostartExecutable } from '../native/build.mjs';
import { installAutostart } from './install-autostart.mjs';

const projectRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const execute = promisify(execFile);
const desktopProbe = `
$ErrorActionPreference = 'Stop'
$locations = @(Get-AppxPackage -Name OpenAI.Codex | Select-Object -ExpandProperty InstallLocation)
$legacy = Join-Path $env:LOCALAPPDATA 'OpenAI\\Codex\\Codex.exe'
if (Test-Path -LiteralPath $legacy) { $locations += Split-Path $legacy -Parent }
ConvertTo-Json -InputObject $locations -Compress
`;
export const stopWidgetCommand = `
$ErrorActionPreference = 'Stop'
$currentSessionId = (Get-Process -Id $PID).SessionId
$expectedPaths = @($env:USAGE_RINGS_WIDGET_PATH)
$shortcutPath = Join-Path $env:APPDATA 'Microsoft\\Windows\\Start Menu\\Programs\\Startup\\Codex Usage Rings.lnk'
if (Test-Path -LiteralPath $shortcutPath) {
    $shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut($shortcutPath)
    if ([IO.Path]::GetFileName($shortcut.TargetPath) -eq 'CodexUsageRingsAutostart.exe') {
        $expectedPaths += Join-Path ([IO.Path]::GetDirectoryName($shortcut.TargetPath)) 'CodexUsageRings.exe'
    }
}
$component = Get-Process | Where-Object { $_.ProcessName -eq 'CodexUsageRings' } |
    Where-Object { $expectedPaths -contains $_.Path -and $_.SessionId -eq $currentSessionId } |
    Select-Object -First 1
if ($component) {
    $component.Kill()
    if (-not $component.WaitForExit(5000)) { throw 'The existing widget did not stop.' }
}
`;

export function deploymentMetadata(root, nodeExecutable, codexCommand) {
  return {
    settingsPath:join(root, 'Usage_Widget_Settings_Private.json'),
    nodeExecutable,
    codexCommand,
  };
}

export async function findExecutables(directory, basename, depth = 0) {
  if (depth > 6) return [];
  let entries;
  try { entries = await readdir(directory, { withFileTypes:true }); }
  catch (error) { if (error.code === 'ENOENT' || error.code === 'EACCES') return []; throw error; }
  const results = [];
  for (const entry of entries) {
    const path = join(directory, entry.name);
    if (entry.isFile() && entry.name.toLowerCase() === basename.toLowerCase()) results.push(path);
    else if (entry.isDirectory()) results.push(...await findExecutables(path, basename, depth + 1));
  }
  return results;
}

export async function inspectCodexCommand(command) {
  try {
    const { stdout } = await execute(command, ['--version'], { windowsHide:true, timeout:15000 });
    const version = /codex(?:-cli)?\s+(\d+\.\d+\.\d+)/i.exec(stdout)?.[1];
    if (!version) return null;
    await execute(command, ['app-server', '--help'], { windowsHide:true, timeout:15000 });
    return { command, version };
  } catch { return null; }
}

async function detectDesktop() {
  const { stdout } = await execute('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', desktopProbe], {
    windowsHide:true, encoding:'utf8',
  });
  const locations = JSON.parse(stdout.replace(/^\uFEFF/, '').trim() || '[]');
  if (!locations.length) throw new Error('Install ChatGPT/Codex Desktop for Windows and sign in, then run Setup.cmd again.');
  return locations;
}

async function locateCodex(desktopLocations, readOnly, npmScript, runtimeDirectory) {
  const candidates = [process.env.USAGE_RINGS_CODEX_COMMAND, process.env.CODEX_CLI_PATH].filter(Boolean);
  try {
    const { stdout } = await execute('where.exe', ['codex.exe'], { windowsHide:true, encoding:'utf8' });
    candidates.push(...stdout.trim().split(/\r?\n/));
  } catch {}
  if (process.env.LOCALAPPDATA) {
    candidates.push(...await findExecutables(join(process.env.LOCALAPPDATA, 'OpenAI', 'Codex', 'bin'), 'codex.exe'));
  }
  candidates.push(...await findExecutables(join(runtimeDirectory, 'codex', 'node_modules', '@openai'), 'codex.exe'));
  const valid = [];
  for (const candidate of [...new Set(candidates)]) {
    const information = await inspectCodexCommand(candidate);
    if (information) valid.push(information);
  }
  valid.sort((first, second) => {
    const firstParts = first.version.split('.').map(Number);
    const secondParts = second.version.split('.').map(Number);
    return secondParts[0] - firstParts[0] || secondParts[1] - firstParts[1] || secondParts[2] - firstParts[2];
  });
  if (valid.length) return valid[0];
  for (const location of desktopLocations) {
    for (const candidate of await findExecutables(join(location, 'app', 'resources'), 'codex.exe')) {
      const information = await inspectCodexCommand(candidate);
      if (information) return information;
    }
  }
  if (readOnly) throw new Error('No working Codex CLI was found. Run Setup.cmd without -CheckOnly to install the local CLI.');
  console.log('Installing the official Codex CLI in the current user runtime directory...');
  const prefix = join(runtimeDirectory, 'codex');
  await run(process.execPath, [npmScript, 'install', '--prefix', prefix, '--no-audit', '--no-fund', '@openai/codex@0.160.0']);
  for (const candidate of await findExecutables(join(prefix, 'node_modules', '@openai'), 'codex.exe')) {
    const information = await inspectCodexCommand(candidate);
    if (information) return information;
  }
  throw new Error('The Codex npm package did not provide a working Windows executable.');
}

async function run(command, argumentsList) {
  const child = spawn(command, argumentsList, { cwd:projectRoot, windowsHide:true, stdio:'inherit' });
  await new Promise((resolve, reject) => {
    child.once('error', reject);
    child.once('exit', code => code === 0 ? resolve() : reject(new Error('A setup command failed with exit code ' + code + ': ' + command)));
  });
}

export async function setup({ check = false } = {}) {
  if (process.platform !== 'win32' || process.arch !== 'x64') throw new Error('This setup supports Windows x64.');
  if (Number(process.versions.node.split('.')[0]) < 24) throw new Error('Node.js 24 or newer is required.');
  const npmScript = join(dirname(process.execPath), 'node_modules', 'npm', 'bin', 'npm-cli.js');
  await access(npmScript);
  await access(join(process.env.WINDIR || process.env.SystemRoot, 'Microsoft.NET', 'Framework64', 'v4.0.30319', 'csc.exe'));
  const desktopLocations = await detectDesktop();
  const runtimeDirectory = join(process.env.LOCALAPPDATA, 'CodexUsageWidget', 'Runtime');
  const codex = await locateCodex(desktopLocations, check, npmScript, runtimeDirectory);
  if (!isAbsolute(codex.command)) throw new Error('The Codex executable path must be absolute.');
  console.log('Project: ' + projectRoot);
  console.log('Node.js: ' + process.versions.node);
  console.log('Codex CLI: ' + codex.version);
  if (check) { console.log('Prerequisite checks passed. No installation settings were changed.'); return; }

  const reportPath = join(projectRoot, 'Setup_Report_Private.json');
  let paused = false, completed = false;
  let previousAutostart = false;
  try {
    try { previousAutostart = JSON.parse(await readFile(join(projectRoot, 'Autostart_Installation_Private.json'), 'utf8')).enabled === true; } catch {}
    console.log('Installing project dependencies...');
    await run(process.execPath, [npmScript, 'ci', '--no-audit', '--no-fund']);
    const metadata = deploymentMetadata(projectRoot, process.execPath, codex.command);
    await writeFile(join(projectRoot, 'native', 'Local_Deployment_Private.json'), JSON.stringify(metadata, null, 2) + '\n', 'utf8');
    console.log('Building and checking the widget...');
    await run(process.execPath, ['build.mjs']);
    await run(process.execPath, ['--test', 'tests/*.test.mjs']);
    const stagedExecutable = join(projectRoot, 'native', 'build', 'CodexUsageRings.Setup.exe');
    await buildNative({ outputPath:stagedExecutable });
    await access(autostartExecutable).catch(async error => {
      if (error.code !== 'ENOENT') throw error;
      await buildAutostart();
    });
    await execute(autostartExecutable, ['--stop'], { windowsHide:true });
    paused = true;
    await execute('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', stopWidgetCommand], {
      windowsHide:true, env:{ ...process.env, USAGE_RINGS_WIDGET_PATH:nativeExecutable },
    });
    await copyFile(stagedExecutable, nativeExecutable);
    await unlink(stagedExecutable);
    await installAutostart();
    completed = true;
    const report = { completedAt:new Date().toISOString(), success:true, projectRoot, ...metadata, codexVersion:codex.version };
    await writeFile(reportPath, JSON.stringify(report, null, 2) + '\n', 'utf8');
    console.log('Setup completed. Usage Rings will start silently with the desktop app and follow new conversations.');
    console.log('Keep this project folder in place. To disable autostart, use npm run autostart:remove.');
  } catch (error) {
    if (paused && previousAutostart && !completed) {
      try { await access(nativeExecutable); await installAutostart(); }
      catch (restoreError) { console.error('Could not restore the previous autostart: ' + restoreError.message); }
    }
    await writeFile(reportPath, JSON.stringify({ success:false, failedAt:new Date().toISOString(), error:error.stack, nativeSetupCompleted:completed }, null, 2) + '\n', 'utf8').catch(() => {});
    throw error;
  }
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  let checking = false;
  try {
    const { values } = parseArgs({ options:{ check:{ type:'boolean' } } });
    checking = values.check === true;
    await setup({ check:checking });
  } catch (error) {
    if (!checking) {
      await writeFile(join(projectRoot, 'Setup_Report_Private.json'), JSON.stringify({
        success:false, failedAt:new Date().toISOString(), error:error.stack || error.message,
      }, null, 2) + '\n', 'utf8').catch(() => {});
    }
    console.error(error.stderr || error.stack || error.message);
    process.exitCode = 1;
  }
}