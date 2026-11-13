// Renders each poster's type layer with Playwright: a transparent overlay, the
// exposure bar's fill on its own, and where the art and the fill sit.
//
// Usage: node posters_overlay.js <jobs.json>
// jobs.json: [{ "html": "...", "out": "<prefix>", "scale": 1, "width": 1600, "height": 2000 }]
// Writes <prefix>.overlay.png, <prefix>.fill.png and <prefix>.json.
const fs = require('fs');
const { chromium } = require('playwright');

// Chromium occasionally fails a capture under heavy CPU load; a retry clears it.
async function shoot(page, path) {
  for (let attempt = 1; ; attempt++) {
    try { return await page.screenshot({ path, omitBackground: true }); }
    catch (e) { if (attempt === 3) throw e; await page.waitForTimeout(500); }
  }
}

(async () => {
  const jobs = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));
  const browser = await chromium.launch();
  for (const job of jobs) {
    console.log('overlay', job.out.split('/').pop(), 'x' + job.scale);
    const page = await browser.newPage({ viewport: { width: job.width || 1600, height: job.height || 2000 }, deviceScaleFactor: job.scale });
    await page.goto('file://' + job.html);
    await page.evaluate(() => document.fonts.ready);
    await page.evaluate(() => window.fit && window.fit());

    const rect = sel => page.evaluate(s => {
      const e = document.querySelector(s);
      if (!e) return null;
      const r = e.getBoundingClientRect();
      return { x: r.x, y: r.y, w: r.width, h: r.height };
    }, sel);
    const geometry = { art: await rect('#art'), fill: await rect('#fill'), scale: job.scale };

    await page.addStyleTag({ content: '#fill { visibility: hidden !important; }' });
    await shoot(page, job.out + '.overlay.png');

    if (geometry.fill) {
      await page.addStyleTag({ content: 'body * { visibility: hidden !important; } #fill { visibility: visible !important; }' });
      await shoot(page, job.out + '.fill.png');
    }
    fs.writeFileSync(job.out + '.json', JSON.stringify(geometry));
    await page.close();
  }
  await browser.close();
})();
