
-- Migration: 022_global_machine_name_unique.sql
-- Enforce globally unique machine names.
-- Does not modify existing machine records.

ALTER TABLE vacuum_machines
ADD CONSTRAINT uq_vacuum_machines_machine_name
UNIQUE (machine_name);
