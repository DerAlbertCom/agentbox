FROM scratch
COPY --chown=1000:1000 files/home/ /home/agent/
# Login shells source it: it sets the terminal colors of the theme when SBOX_TERMINAL_THEME=1.
COPY --chown=0:0 files/etc/profile.d/ /etc/profile.d/
# Point the apps at the theme files sbox theme renders, so their own config files stay the user's.
ENV BAT_THEME=sbox
ENV FZF_DEFAULT_OPTS_FILE=/home/agent/.config/fzf/sbox
# lazygit reads the user's config and then the theme file.
ENV LG_CONFIG_FILE=/home/agent/.config/lazygit/config.yml,/home/agent/.config/lazygit/sbox.yml
