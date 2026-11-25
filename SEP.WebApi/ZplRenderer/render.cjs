const { createInterface } = require("node:readline");
const { readFile } = require("node:fs/promises");

const { init, zplToBase64Async } = require("zpl-renderer-js/external");

async function main() {
  const wasmBytes = await readFile(require.resolve("zpl-renderer-js/wasm"));
  await init({ wasmBytes });

  if (process.send) {
    process.send({ ready: true });
  }

  const rl = createInterface({ input: process.stdin, crlfDelay: Infinity });
  rl.on("line", (line) => {
    void handleLine(line);
  });
}

async function handleLine(line) {
  if (!line.trim()) {
    return;
  }

  let request;
  try {
    request = JSON.parse(line);
  } catch (error) {
    write({ id: null, error: `Invalid JSON request: ${error.message}` });
    return;
  }

  try {
    const pngBase64 = await zplToBase64Async(
      request.zpl,
      request.widthMm ?? undefined,
      request.heightMm ?? undefined,
      request.dpmm ?? undefined,
      {
        grayscaleOutput: request.grayscaleOutput === true,
        enableInvertedLabels: request.enableInvertedLabels === true,
      }
    );

    write({ id: request.id, pngBase64 });
  } catch (error) {
    write({ id: request.id, error: error?.message ?? String(error) });
  }
}

function write(value) {
  process.stdout.write(`${JSON.stringify(value)}\n`);
}

main().catch((error) => {
  process.stderr.write(`${error?.stack ?? error}\n`);
  process.exitCode = 1;
});
