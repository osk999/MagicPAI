const { chromium } = require('playwright');
const fs = require('fs/promises');
const path = require('path');

const dashboardUrl = 'http://127.0.0.1:5001/magic/dashboard';
const workdir = 'C:\\AllGit\\CSharp\\KaTTzMonoRepo';
const prompt = `Today this sytem support only normal stocks. I want to allow new learning with crypto.
1. need 20 best crypto btc/usdt , eth/usdt ... think like that with some usd.
2. need to fill the stock daily,minitues bar for 024,2025,2026
3.  need to see if the alpaca api support it if so get the data from there

** problems : I dont want to mix between the current stocks that came from us markets, to this one so need carefully plan how to  do it ? maybe empty db for crypto? maybe diffrent table? need to do research.

/ crypto market working 24 hours not like us market.
 / need find all diffrence and make adaption

*** when you run it with the full orchestrator then you need verify the entire proccess working updates, need to see that new jobs working with crypto, runner also support it.  I want to spreate these two markets entirely.`;

const outputDir = path.resolve('artifacts', 'ui-e2e');
const maxRuntimeMs = 25 * 60 * 1000;
const pollIntervalMs = 10000;

function stamp() {
  return new Date().toISOString();
}

function shortText(value, max = 800) {
  if (!value) return '';
  return value.length > max ? value.slice(-max) : value;
}

async function textOrEmpty(locator) {
  if (await locator.count() === 0) return '';
  return (await locator.first().textContent())?.trim() ?? '';
}

async function readUiState(page) {
  const status = await textOrEmpty(page.locator('h3 .status-badge'));
  const output = await textOrEmpty(page.locator('pre.output-content'));
  const verification = await textOrEmpty(page.locator('.verification-badge'));
  const container = await textOrEmpty(page.locator('.container-status'));
  const dag = await page.locator('.dag-node').evaluateAll(nodes =>
    nodes.map(node => {
      const name = node.querySelector('.dag-name')?.textContent?.trim() ?? '';
      const statusText = node.querySelector('.dag-status')?.textContent?.trim() ?? '';
      return { name, status: statusText };
    })
  );

  return { status, output, verification, container, dag };
}

async function saveJson(fileName, data) {
  await fs.writeFile(path.join(outputDir, fileName), JSON.stringify(data, null, 2));
}

async function main() {
  await fs.mkdir(outputDir, { recursive: true });

  const browser = await chromium.launch({ headless: true });
  const page = await browser.newPage({ viewport: { width: 1440, height: 1200 } });

  const consoleMessages = [];
  const pageErrors = [];

  page.on('console', message => {
    const line = `[${stamp()}] [console:${message.type()}] ${message.text()}`;
    consoleMessages.push(line);
    console.log(line);
  });

  page.on('pageerror', error => {
    const line = `[${stamp()}] [pageerror] ${error.stack || error.message}`;
    pageErrors.push(line);
    console.log(line);
  });

  try {
    console.log(`[${stamp()}] opening ${dashboardUrl}`);
    await page.goto(dashboardUrl, { waitUntil: 'networkidle' });
    await page.screenshot({ path: path.join(outputDir, '01-dashboard.png'), fullPage: true });

    await page.locator('textarea.form-input').fill(prompt);
    await page.locator('input.form-input').fill(workdir);

    const selectedAssistant = await page.locator('select.agent-selector').inputValue();
    console.log(`[${stamp()}] selected assistant: ${selectedAssistant}`);

    await page.screenshot({ path: path.join(outputDir, '02-filled-dashboard.png'), fullPage: true });

    await Promise.all([
      page.waitForURL(/\/magic\/sessions\//, { timeout: 60000 }),
      page.getByRole('button', { name: 'Start Session' }).click()
    ]);

    const sessionUrl = page.url();
    const sessionId = sessionUrl.split('/').pop() ?? '';
    console.log(`[${stamp()}] session started: ${sessionId}`);
    await page.screenshot({ path: path.join(outputDir, '03-session-start.png'), fullPage: true });

    const timeline = [];
    const start = Date.now();
    let lastSnapshot = '';
    let lastStatus = '';

    while (Date.now() - start < maxRuntimeMs) {
      await page.waitForTimeout(pollIntervalMs);

      const ui = await readUiState(page);
      const snapshot = JSON.stringify({
        status: ui.status,
        verification: ui.verification,
        container: ui.container,
        dag: ui.dag
      });

      if (snapshot !== lastSnapshot || ui.status !== lastStatus) {
        const entry = {
          at: stamp(),
          status: ui.status,
          verification: ui.verification,
          container: ui.container,
          dag: ui.dag,
          outputTail: shortText(ui.output, 2000)
        };
        timeline.push(entry);
        console.log(`[${entry.at}] status=${entry.status || '<empty>'} dag=${entry.dag.length} verification=${entry.verification ? 'yes' : 'no'}`);
        if (entry.outputTail) {
          console.log(`[${entry.at}] output tail:\n${entry.outputTail}`);
        }
        const shotName = `session-${timeline.length.toString().padStart(2, '0')}.png`;
        await page.screenshot({ path: path.join(outputDir, shotName), fullPage: true });
        lastSnapshot = snapshot;
        lastStatus = ui.status;
      }

      if (ui.status && ui.status.toLowerCase() !== 'running' && ui.status.toLowerCase() !== 'idle') {
        console.log(`[${stamp()}] terminal session state reached: ${ui.status}`);
        break;
      }
    }

    const finalUi = await readUiState(page);
    const summary = {
      sessionUrl,
      sessionId,
      assistant: selectedAssistant,
      workdir,
      finalState: finalUi.status,
      finalVerification: finalUi.verification,
      finalContainer: finalUi.container,
      finalDag: finalUi.dag,
      finalOutputTail: shortText(finalUi.output, 4000),
      timeline,
      consoleMessages,
      pageErrors
    };

    await page.screenshot({ path: path.join(outputDir, '99-final.png'), fullPage: true });
    await saveJson('summary.json', summary);
    await saveJson('console.json', { consoleMessages, pageErrors });

    console.log(`[${stamp()}] summary written to ${path.join(outputDir, 'summary.json')}`);
  } finally {
    await browser.close();
  }
}

main().catch(error => {
  console.error(error);
  process.exitCode = 1;
});
