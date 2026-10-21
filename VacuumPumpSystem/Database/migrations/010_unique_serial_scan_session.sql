-- Check for duplicate sessions before running this migration:
-- SELECT line_id, product_serial, COUNT(*) AS duplicate_count
-- FROM serial_scan_sessions
-- GROUP BY line_id, product_serial
-- HAVING COUNT(*) > 1;
-- Resolve any duplicates before adding the unique key.

ALTER TABLE serial_scan_sessions
    ADD UNIQUE KEY uq_serial_scan_sessions_line_serial (line_id, product_serial);
