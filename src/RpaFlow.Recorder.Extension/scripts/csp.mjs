import { readFile, readdir } from "node:fs/promises";
import { extname, join, relative } from "node:path";

const dynamicCode = /\b(?:eval|Function)\s*\(/u;

export async function assertNoDynamicCode(directory) {
  const files = (await readdir(directory, { recursive: true }))
    .filter((path) => extname(path) === ".js");
  for (const file of files) {
    const source = await readFile(join(directory, file), "utf8");
    if (dynamicCode.test(source)) {
      throw new Error(
        `Avaliação dinâmica incompatível com a CSP MV3 em ${relative(directory, join(directory, file))}.`
      );
    }
  }
}
