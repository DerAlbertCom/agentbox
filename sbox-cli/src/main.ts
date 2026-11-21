import { sbox } from "./sbox";

// A reader that closes the pipe early, such as `sbox theme --list | head`, has all it wants.
process.stdout.on("error", (error: NodeJS.ErrnoException) => {
  if (error.code !== "EPIPE") throw error;
  process.exit(process.exitCode ?? 0);
});

process.exitCode = await sbox(process.argv.slice(2), {
  env: process.env,
  stdout: process.stdout,
  stderr: process.stderr,
  stdin: process.stdin,
});
