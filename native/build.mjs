import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { access, mkdir } from 'node:fs/promises';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

export const nativeRoot = dirname(fileURLToPath(import.meta.url));
export const nativeExecutable = join(nativeRoot, 'build', 'CodexUsageRings.exe');
export const autostartExecutable = join(nativeRoot, 'build', 'CodexUsageRingsAutostart.exe');

export async function buildNative({ outputPath = nativeExecutable } = {}) {
  if (process.platform !== 'win32') throw new Error('The native widget currently supports Windows only.');
  const framework = join(process.env.WINDIR || process.env.SystemRoot, 'Microsoft.NET', 'Framework64', 'v4.0.30319');
  const compiler = join(framework, 'csc.exe');
  await access(compiler);
  await mkdir(join(nativeRoot, 'build'), { recursive:true });
  const sources = ['Anchor.cs', 'Vision.cs', 'Theme.cs', 'PositionSwitch.cs', 'DetailsPlacement.cs', 'Preferences.cs', 'Widget.cs'].map(name => join(nativeRoot, name));
  const references = ['System.Drawing.dll', 'System.Windows.Forms.dll', 'System.Web.Extensions.dll']
    .map(name => '/reference:' + join(framework, name));
  references.push(...['UIAutomationClient.dll', 'UIAutomationTypes.dll', 'WindowsBase.dll']
    .map(name => '/reference:' + join(framework, 'WPF', name)));
  const { stdout, stderr } = await promisify(execFile)(compiler, [
    '/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/out:' + outputPath,
    ...references, ...sources,
  ], { windowsHide:true, encoding:'utf8', maxBuffer:1024*1024 });
  if (stdout.trim()) console.log(stdout.trim());
  if (stderr.trim()) console.error(stderr.trim());
  return outputPath;
}

export async function buildAutostart() {
  if (process.platform !== 'win32') throw new Error('The autostart supervisor currently supports Windows only.');
  const framework = join(process.env.WINDIR || process.env.SystemRoot, 'Microsoft.NET', 'Framework64', 'v4.0.30319');
  await mkdir(join(nativeRoot, 'build'), { recursive:true });
  await promisify(execFile)(join(framework, 'csc.exe'), [
    '/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/out:' + autostartExecutable,
    '/reference:' + join(framework, 'System.Web.Extensions.dll'), join(nativeRoot, 'Autostart.cs'),
  ], { windowsHide:true, encoding:'utf8', maxBuffer:1024*1024 });
  return autostartExecutable;
}

if (process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1]) {
  try { console.log('Built: ' + await (process.argv.includes('--autostart') ? buildAutostart() : buildNative())); }
  catch (error) { console.error(error.stdout || error.message); process.exitCode = 1; }
}