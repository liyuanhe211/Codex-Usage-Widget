import { pathToFileURL } from 'node:url';
export function sessionStartOutput() { return {}; }
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  process.stdout.write('{}\n');
}
