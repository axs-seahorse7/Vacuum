CREATE TABLE IF NOT EXISTS production_lines (
    id INT AUTO_INCREMENT PRIMARY KEY,

    line_code VARCHAR(100) NOT NULL,
    line_name VARCHAR(150) NOT NULL,
    description VARCHAR(255) NULL,

    is_active BOOLEAN NOT NULL DEFAULT TRUE,

    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT uq_production_lines_code
        UNIQUE (line_code)
);