FROM scratch
COPY --chown=1000:1000 --chmod=0755 files/home/.local/bin/ /home/agent/.local/bin/
