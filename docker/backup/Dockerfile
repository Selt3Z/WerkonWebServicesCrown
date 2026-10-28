FROM postgres:17-alpine

# util-linux — flock; coreutils — timeout для restic prune; tzdata — TZ
RUN apk add --no-cache bash tzdata restic util-linux coreutils

COPY backup.sh /usr/local/bin/backup.sh
COPY restore-test.sh /usr/local/bin/restore-test.sh
RUN chmod +x /usr/local/bin/backup.sh /usr/local/bin/restore-test.sh

CMD ["/usr/local/bin/backup.sh"]
