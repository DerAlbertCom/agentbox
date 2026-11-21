FROM scratch
COPY --chown=1000:1000 files/home/ /home/agent/
# The apps whose own config files stay the user's find the theme sbox theme renders here.
ENV BAT_THEME=sbox
# lazygit reads the user's config and then the theme file.
ENV LG_CONFIG_FILE=/home/agent/.config/lazygit/config.yml,/home/agent/.config/lazygit/sbox.yml
