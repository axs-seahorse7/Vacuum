
CREATE TABLE IF NOT EXISTS pressure_test_records (
    id BIGINT NOT NULL AUTO_INCREMENT,

    line_id INT NOT NULL,
    machine_id INT NOT NULL,

    dummy_code VARCHAR(50) NOT NULL,
    product_serial_no VARCHAR(100) NOT NULL,

    pressure_value DECIMAL(10,3) NOT NULL,
    tested_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,

    PRIMARY KEY (id),

    INDEX idx_test_product (product_serial_no),
    INDEX idx_test_machine_time (machine_id, tested_at),
    INDEX idx_test_line_time (line_id, tested_at),

    CONSTRAINT fk_test_line
        FOREIGN KEY (line_id)
        REFERENCES production_lines(id),

    CONSTRAINT fk_test_machine
        FOREIGN KEY (machine_id)
        REFERENCES vacuum_machines(id)
);
