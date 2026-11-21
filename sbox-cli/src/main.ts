import { sbox } from "./sbox";

process.exitCode = await sbox(process.argv.slice(2), {
  env: process.env,
  stdout: process.stdout,
  stderr: process.stderr,
  stdin: process.stdin,
});
