CREATE TABLE IF NOT EXISTS product_mappings (
    id BIGINT AUTO_INCREMENT PRIMARY KEY,

    machine_id INT NOT NULL,

    product_code VARCHAR(100) NOT NULL,

    mapped_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,

    unmapped_at DATETIME NULL,

    CONSTRAINT fk_product_mapping_machine
        FOREIGN KEY (machine_id)
        REFERENCES vacuum_machines(id)
        ON DELETE CASCADE,

    INDEX idx_product_code (product_code),

    INDEX idx_machine_id (machine_id)
);