CREATE TABLE IF NOT EXISTS vacuum_machines (
    id INT AUTO_INCREMENT PRIMARY KEY,

    line_id INT NOT NULL,

    dummy_code VARCHAR(50) NOT NULL,
    modbus_address TINYINT UNSIGNED NOT NULL,
    machine_name VARCHAR(150) NOT NULL,

    current_pressure DECIMAL(10,3) NULL,
    is_online BOOLEAN NOT NULL DEFAULT FALSE,
    last_seen_at DATETIME NULL,

    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT fk_machine_line
        FOREIGN KEY (line_id)
        REFERENCES production_lines(id),

    CONSTRAINT uq_machine_dummy
        UNIQUE (dummy_code),

    CONSTRAINT uq_line_modbus
        UNIQUE (line_id, modbus_address)
);