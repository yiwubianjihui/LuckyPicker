// Web UI 冒烟测试（jsdom）
const { JSDOM } = require('jsdom');
const fs = require('fs');
const path = require('path');

const html = fs.readFileSync(path.join(__dirname, 'index.html'), 'utf8');
const pakoSrc = fs.readFileSync(path.join(__dirname, 'pako.min.js'), 'utf8');
const appSrc = fs.readFileSync(path.join(__dirname, 'app.js'), 'utf8');

let pass = 0, fail = 0;
function check(name, ok) { console.log((ok ? 'PASS  ' : 'FAIL  ') + name); ok ? pass++ : fail++; }

const dom = new JSDOM(html, { runScripts: 'outside-only', url: 'file:///tmp/index.html', pretendToBeVisual: true });
const w = dom.window;
w.confirm = () => true;
w.alert = () => {};
try { w.eval(pakoSrc); } catch (e) { console.log('pako eval fail', e.message); }
try { w.eval(appSrc); } catch (e) { console.log('app eval fail', e.message); process.exit(1); }

const doc = w.document;
setTimeout(() => {
  // 1) 班级选择模态框
  check('class modal shown', !!doc.querySelector('.class-grid .btn'));
  const classBtns = doc.querySelectorAll('.class-grid .btn');
  check('class buttons >= 3', classBtns.length >= 3);
  // 2) 选择班级
  classBtns[0].dispatchEvent(new w.MouseEvent('click', { bubbles: true }));
  check('main rendered', !!doc.getElementById('pickBtn'));
  check('class select populated', doc.querySelectorAll('#classSelect option').length >= 3);
  // 主界面完整性：innerHTML 拼接一旦被截断（如漏写 +），筛选/抽取区会全部丢失
  check('main fully built', !!doc.getElementById('classSelect') && !!doc.getElementById('pickBtn') &&
    !!doc.getElementById('blockInput') && !!doc.getElementById('voiceBadge'));
  // 「下载客户端」按钮必须在 banner 中渲染出来
  const dlLink = doc.querySelector('.banner a.btn-blue[href*="lr.yibianhui.cn"]');
  check('download btn in banner', !!dlLink);
  const badge = doc.getElementById('classBadge');
  check('badge shows class', badge && badge.textContent.indexOf('当前班级') >= 0);
  // 3) 抽一人（动画后落定）
  const pn = doc.getElementById('pickedName');
  doc.getElementById('pickBtn').dispatchEvent(new w.MouseEvent('click', { bubbles: true }));
  setTimeout(() => {
    check('picked name set', pn && pn.textContent.length > 0 && pn.textContent !== '——');
    // 4) 连抽五人
    doc.getElementById('multiBtn').dispatchEvent(new w.MouseEvent('click', { bubbles: true }));
    setTimeout(() => {
      const chips = doc.querySelectorAll('#multiNames .chip');
      check('multi chips = 5', chips.length === 5);
      // 5) 屏蔽
      const inp = doc.getElementById('blockInput');
      const first = doc.querySelector('#classSelect option').value;
      let nm = '';
      const st = w.LuckyPicker; // 不可直接读 state；用 DOM 反推：屏蔽一个学生名
      // 从 select 找班级后读取？简化：直接屏蔽“不存在的名字”应提示
      inp.value = '不存在的学生XYZ';
      doc.getElementById('addBlockBtn').dispatchEvent(new w.MouseEvent('click', { bubbles: true }));
      check('unknown block hint', doc.getElementById('hint').textContent.indexOf('未找到') >= 0);
      // 6) 名单管理
      doc.getElementById('btnEditor').dispatchEvent(new w.MouseEvent('click', { bubbles: true }));
      check('editor opened', !!doc.getElementById('stuBody'));
      const rows = doc.querySelectorAll('#stuBody tr');
      check('editor rows = demo roster', rows.length === 50);
      // 7) 设置页
      const tabs = doc.querySelectorAll('.tab');
      for (let i = 0; i < tabs.length; i++) if (tabs[i].textContent.indexOf('语音设置') >= 0) tabs[i].dispatchEvent(new w.MouseEvent('click', { bubbles: true }));
      check('settings tab', !!doc.getElementById('srcSel') && !!doc.getElementById('voiceSel'));
      // 8) 悬浮球：默认创建、设置里可开关、单击可抽取
      const ball = doc.getElementById('lp-ball');
      check('ball created', !!ball);
      check('ball toggle in settings', !!doc.getElementById('swBall'));
      if (ball) {
        ball.dispatchEvent(new w.MouseEvent('pointerdown', { bubbles: true }));
        ball.dispatchEvent(new w.MouseEvent('pointerup', { bubbles: true }));
      }
      setTimeout(function () {
        check('ball click picks', doc.getElementById('pickedName').textContent !== '——');
        console.log('----');
        console.log(fail === 0 ? 'ALL PASS (' + pass + ')' : fail + ' FAILURES of ' + (pass + fail));
        process.exit(fail === 0 ? 0 : 1);
      }, 1200);
    }, 1200);
  }, 1200);
}, 300);
