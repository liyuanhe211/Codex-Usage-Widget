import { build } from 'esbuild';
import { readFile, writeFile, mkdir } from 'node:fs/promises';

const result = await build({
  entryPoints:['public/widget.mjs'], bundle:true, minify:true, write:false,
  format:'esm', target:'es2022', platform:'browser', legalComments:'none',
});
const template = await readFile('public/widget.html', 'utf8');
const javascript = result.outputFiles[0].text.replaceAll('</script', '<\\/script');
await mkdir('dist', { recursive:true });
await writeFile('dist/widget.html', template.replace('/*__WIDGET_BUNDLE__*/', () => javascript), 'utf8');
console.log('Bundled the usage rings view.');
