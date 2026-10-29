const { JSDOM } = require('../web/node_modules/jsdom');
const fs = require('fs');
const path = require('path');

const html = fs.readFileSync(path.join(__dirname, 'index.html'), 'utf8');
const dom = new JSDOM(html, { url: 'https://lr.yibianhui.cn/' });
const doc = dom.window.document;
let pass = 0, fail = 0;
function check(name, ok) { console.log((ok ? 'PASS  ' : 'FAIL  ') + name); ok ? pass++ : fail++; }

check('page title', doc.title.includes('YBH幸运摇人器'));
check('current version', doc.querySelector('.ver')?.textContent.includes('1.1+19'));
const mainDownload = doc.querySelector('a.dl-main');
check('main Windows download', mainDownload?.getAttribute('href') === 'download/LuckyPicker-win-setup-1.1+build19.exe');
check('download options rendered', doc.querySelectorAll('.more-body .opt').length === 7);
const optionLinks = [...doc.querySelectorAll('.more-body a.go')].map(a => a.getAttribute('href'));
check('WPF portable link', optionLinks.includes('download/LuckyPicker-win-wpf-1.1+build19.zip'));
check('Android link', optionLinks.some(href => href?.endsWith('.apk')));
check('Linux link', optionLinks.some(href => href?.endsWith('.zip') && href.includes('linux')));
check('Web link', optionLinks.includes('web/'));
const githubLinks = [...doc.querySelectorAll('a[href*="github.com"]')].map(a => a.href);
check('GitHub links use project repository', githubLinks.length >= 4 && githubLinks.every(href => href.startsWith('https://github.com/Yibianhui/LuckyPicker')));
check('SHA-256 format', /^[0-9A-F]{64}$/.test(doc.getElementById('sha')?.textContent.trim() || ''));
check('latest changelog entry', doc.querySelector('.tl .ev b')?.textContent.includes('1.1+19'));

console.log('----');
console.log(fail === 0 ? 'ALL PASS (' + pass + ')' : fail + ' FAILURES of ' + (pass + fail));
process.exit(fail === 0 ? 0 : 1);
