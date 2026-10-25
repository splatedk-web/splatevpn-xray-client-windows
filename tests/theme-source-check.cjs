const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const crypto = require('node:crypto');
const vm = require('node:vm');
const root = path.resolve(__dirname, '..');
const android = path.resolve(process.argv[2] || path.join(root, '../SplateClient'));
const source = fs.readFileSync(path.join(android, 'app/src/main/java/com/danieldk/splatevpn/ui/theme/Color.kt'), 'utf8');
const palettes = JSON.parse(fs.readFileSync(path.join(root, 'wwwroot/js/palettes.json'), 'utf8'));
const context = { window: {} };
vm.runInNewContext(fs.readFileSync(path.join(root, 'wwwroot/js/palettes.js'), 'utf8'), context);
assert.deepEqual(JSON.parse(JSON.stringify(context.window.SplatePalettes)), palettes, 'native and browser palettes agree');
const map = { background:'bg-dark', backgroundDeep:'bg-deep', surface:'surface-dark', surfaceElevated:'surface-elevated', borderSubtle:'border-subtle', borderMedium:'border-medium', textPrimary:'text-primary', textSecondary:'text-secondary', textTertiary:'text-tertiary', accent:'accent-cyan', accentCyan:'accent-cyan', accentConnected:'accent-connected', accentSecondary:'accent-secondary', accentEmerald:'accent-emerald', accentAmber:'accent-amber', accentCrimson:'accent-crimson', accentViolet:'accent-violet', accentBlue:'accent-blue', onAccent:'on-accent', ambientB:'ambient-b', ambientC:'ambient-c', glassBorderLight:'glass-border-light', glassBorderDark:'glass-border-dark', glassSpecular:'glass-specular' };
for (const match of source.matchAll(/val (\w+)ThemeColors = (?:buildPalette\(|LightThemeColors\.copy\()([\s\S]*?)^\)/gm)) {
  const key = match[1].toLowerCase(), block = match[2];
  assert(palettes[key], key);
  for (const color of block.matchAll(/(\w+)\s*=\s*(?:Color\(0x([A-Fa-f0-9]{8})\)|Color\.(White|Black))/g)) {
    const token = map[color[1]];
    if (!token) continue;
    const hex = color[2] || (color[3] === 'White' ? 'FFFFFFFF' : 'FF000000');
    const expected = '#' + hex.slice(2) + (hex.slice(0, 2) === 'FF' ? '' : hex.slice(0, 2));
    assert.equal(palettes[key][token].toUpperCase(), expected.toUpperCase(), key + ' ' + token);
  }
  const backdrop = block.match(/backdrop\s*=\s*BackdropStyle\.(\w+)/);
  assert.equal(palettes[key].backdrop, backdrop[1]);
  const alpha = block.match(/glassAlpha\s*=\s*([.\d]+)f/);
  if (alpha) assert.equal(palettes[key].glassAlpha, Number(alpha[1]));
}
for (const [name, original] of [['pony','theme_pony_characters.png'], ['winx','theme_bloom_character.png']]) {
  const hash = p => crypto.createHash('sha256').update(fs.readFileSync(p)).digest('hex');
  assert.equal(hash(path.join(root, 'wwwroot/assets/themes', name + '.png')), hash(path.join(android, 'app/src/main/res/drawable-nodpi', original)), name + ' original artwork');
}
console.log('PASS palettes match current Android source; native/browser parity; original Pony/Bloom PNG hashes');
