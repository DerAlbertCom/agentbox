# Sets the terminal colors of the sbox theme in login shells and resets them when the shell
# exits, when abox asks for it with SBOX_TERMINAL_THEME=1. sbox theme renders the script.
if [ "${SBOX_TERMINAL_THEME:-}" = 1 ] && [ -t 1 ] && [ -f "$HOME/.config/sbox/terminal-colors.sh" ]; then
  sh "$HOME/.config/sbox/terminal-colors.sh" 2>/dev/null
  trap 'printf "\033]104\007\033]110\007\033]111\007\033]112\007"' EXIT
fi
