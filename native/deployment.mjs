import { readFileSync, existsSync, readdirSync } from 'node:fs';
import { dirname, isAbsolute, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const defaultMetadataPath = join(dirname(fileURLToPath(import.meta.url)), 'Local_Deployment_Private.json');

export function resolveCodexCommand({ environment = process.env, metadataPath = defaultMetadataPath } = {}) {
  if (environment.USAGE_RINGS_CODEX_COMMAND) return environment.USAGE_RINGS_CODEX_COMMAND;
  if (environment.CODEX_CLI_PATH) return environment.CODEX_CLI_PATH;
  try {
    const metadata = JSON.parse(readFileSync(metadataPath, 'utf8'));
    if (typeof metadata.codexCommand === 'string' && isAbsolute(metadata.codexCommand) && existsSync(metadata.codexCommand)) {
      return metadata.codexCommand;
    }
  } catch {}
  if (environment.LOCALAPPDATA) {
    const directory = join(environment.LOCALAPPDATA, 'OpenAI', 'Codex', 'bin');
    try {
      for (const name of readdirSync(directory)) {
        const candidate = join(directory, name, 'codex.exe');
        if (existsSync(candidate)) return candidate;
      }
    } catch {}
  }
  return 'codex';
}