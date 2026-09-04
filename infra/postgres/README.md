# PostgreSQL and PostGIS

The development database uses the official `postgis/postgis:18-3.6` image. Schema changes are managed by Entity Framework Core migrations, not ad-hoc initialization scripts.

## STU backup policy

The worker creates full PostgreSQL custom-format archives (`.dump`) in the persistent `stu-operations` volume. The global administrator can configure one weekly execution, request an immediate backup, inspect its SHA-256 hash, and download completed archives from the isolated administration portal.

The default policy runs every Sunday at 00:00 in `America/Bahia` and keeps the eight newest archive files. Changing retention never removes execution history or audit records.

Server-local backups improve operational recovery but do not protect against total loss of the host. Copy completed archives periodically to restricted off-host storage and keep access limited to the people authorized to restore the STU database.

## Restore rehearsal

Always validate a downloaded archive in a new, isolated database before considering it recoverable. Never restore an untrusted archive: PostgreSQL archives can contain commands defined by database objects.

1. Download the desired archive from **Administração global → Backups** and compare the SHA-256 shown in the portal:

   ```powershell
   Get-FileHash -Algorithm SHA256 -LiteralPath 'C:\caminho\stu-backup.dump'
   ```

2. Check that PostgreSQL can read the archive table of contents:

   ```powershell
   docker compose cp 'C:\caminho\stu-backup.dump' database:/tmp/stu-restore.dump
   docker compose exec database pg_restore --list /tmp/stu-restore.dump
   ```

3. Create an empty rehearsal database and restore with immediate failure on an error:

   ```powershell
   docker compose exec database createdb -U stu -T template0 stu_restore_test
   docker compose exec database pg_restore -U stu -d stu_restore_test --exit-on-error --no-owner --no-privileges /tmp/stu-restore.dump
   ```

4. Validate the migration history and essential counts without exposing record contents:

   ```powershell
   docker compose exec database psql -U stu -d stu_restore_test -c 'SELECT COUNT(*) FROM "__EFMigrationsHistory";'
   docker compose exec database psql -U stu -d stu_restore_test -c 'SELECT COUNT(*) FROM health_units;'
   docker compose exec database psql -U stu -d stu_restore_test -c 'SELECT COUNT(*) FROM properties;'
   ```

5. Record the rehearsal date, archive ID, hash, duration, and result. A production replacement requires an approved maintenance window, a fresh pre-restore backup, stopped API/worker writers, and explicit confirmation of the destination database. The operational target is completion within eight business hours.
