import { readdir } from 'node:fs/promises';
import { DatabaseSync } from 'node:sqlite';
import { homedir } from 'node:os';
import { join } from 'node:path';
import { THREAD_ID_PATTERN } from '../usage.mjs';

// Page titles identify candidates only; bind exclusively to one exact indexed ID.
export class PageThreadReader {
  constructor(codexDirectory = process.env.CODEX_HOME || join(homedir(), '.codex')) {
    this.codexDirectory = codexDirectory;
  }
  async read(pageTitle) {
    if (typeof pageTitle !== 'string' || !pageTitle.trim()
        || ['Codex', 'ChatGPT', 'New chat', 'New thread'].includes(pageTitle)) return null;
    try {
      const files = (await readdir(this.codexDirectory))
        .map(name => ({ name, version:Number(/^state_(\d+)\.sqlite$/.exec(name)?.[1]) }))
        .filter(file => Number.isInteger(file.version))
        .sort((first, second) => second.version - first.version);
      if (!files.length) return null;
      const database = new DatabaseSync(join(this.codexDirectory, files[0].name), { readOnly:true });
      try {
        const columns = database.prepare('PRAGMA table_info(threads)').all().map(column => column.name);
        if (!columns.includes('id') || !columns.includes('title')) return null;
        const displayName = columns.includes('name') ? "coalesce(nullif(name, ''), title)" : 'title';
        const matches = database.prepare('SELECT id FROM threads WHERE ' + displayName + ' = ? LIMIT 2').all(pageTitle);
        return matches.length === 1 && THREAD_ID_PATTERN.test(matches[0].id) ? matches[0].id : null;
      } finally { database.close(); }
    } catch { return null; }
  }
}

export class DesktopPageBinding {
  constructor() {
    this.observed = false;
    this.pageTitle = null;
    this.pageRevision = null;
    this.windowCount = 1;
    this.generation = 0;
  }
  update(request) {
    const windowCount = Number.isInteger(request.windowCount) && request.windowCount >= 0 ? request.windowCount : 1;
    const observed = this.observed || Object.hasOwn(request, 'pageTitle');
    const pageTitle = Object.hasOwn(request, 'pageTitle')
      ? typeof request.pageTitle === 'string' ? request.pageTitle : null : this.pageTitle;
    const pageRevision = Object.hasOwn(request, 'pageTitle')
      ? Number.isSafeInteger(request.pageRevision) && request.pageRevision >= 0 ? request.pageRevision : null
      : this.pageRevision;
    if (observed === this.observed && pageTitle === this.pageTitle
        && pageRevision === this.pageRevision && windowCount === this.windowCount) return false;
    this.observed = observed;
    this.pageTitle = pageTitle;
    this.pageRevision = pageRevision;
    this.windowCount = windowCount;
    this.generation++;
    return true;
  }
  isCurrent(generation) { return generation === this.generation; }
}
