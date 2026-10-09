
ALTER TABLE production_lines
ADD COLUMN lora_channel SMALLINT UNSIGNED NULL
AFTER line_name;
