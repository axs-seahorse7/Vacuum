-- 009_add_line_scan_policy_and_lifecycle.sql
-- Local development migration for line-wise scan rules and per-serial lifecycle.
-- Requires MySQL 8.0.16+ for enforced CHECK constraints.

CREATE TABLE IF NOT EXISTS line_scan_policies (
    id INT NOT NULL AUTO_INCREMENT,
    line_id INT NOT NULL,
    required_scan_count INT NOT NULL,
    minimum_interval_minutes INT NOT NULL,
    maximum_pressure DECIMAL(12, 6) NOT NULL,
    is_active TINYINT(1) NOT NULL DEFAULT 1,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
        ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    CONSTRAINT uq_line_scan_policies_line UNIQUE (line_id),
    CONSTRAINT fk_line_scan_policies_line
        FOREIGN KEY (line_id) REFERENCES production_lines(id)
        ON DELETE RESTRICT ON UPDATE CASCADE,
    CONSTRAINT chk_line_scan_policy_count CHECK (required_scan_count BETWEEN 1 AND 10),
    CONSTRAINT chk_line_scan_policy_interval CHECK (minimum_interval_minutes >= 0),
    CONSTRAINT chk_line_scan_policy_pressure CHECK (maximum_pressure > 0)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS serial_scan_sessions (
    id BIGINT NOT NULL AUTO_INCREMENT,
    line_id INT NOT NULL,
    product_serial VARCHAR(191) NOT NULL,
    required_scan_count INT NOT NULL,
    minimum_interval_minutes INT NOT NULL,
    maximum_pressure DECIMAL(12, 6) NOT NULL,
    accepted_scan_count INT NOT NULL DEFAULT 0,
    status ENUM('IN_PROGRESS', 'COMPLETED') NOT NULL DEFAULT 'IN_PROGRESS',
    started_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    last_scanned_at DATETIME(3) NULL,
    completed_at DATETIME(3) NULL,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
        ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    CONSTRAINT uq_serial_scan_session_line_serial UNIQUE (line_id, product_serial),
    CONSTRAINT fk_serial_scan_session_line
        FOREIGN KEY (line_id) REFERENCES production_lines(id)
        ON DELETE RESTRICT ON UPDATE CASCADE,
    CONSTRAINT chk_serial_scan_session_count CHECK (
        required_scan_count BETWEEN 1 AND 10
        AND accepted_scan_count >= 0
        AND accepted_scan_count <= required_scan_count
    ),
    CONSTRAINT chk_serial_scan_session_interval CHECK (minimum_interval_minutes >= 0),
    CONSTRAINT chk_serial_scan_session_pressure CHECK (maximum_pressure > 0)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS serial_scan_events (
    id BIGINT NOT NULL AUTO_INCREMENT,
    session_id BIGINT NOT NULL,
    scan_number INT NOT NULL,
    machine_id INT NULL,
    dummy_code VARCHAR(100) NOT NULL,
    pressure_value DECIMAL(12, 6) NOT NULL,
    scanned_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    PRIMARY KEY (id),
    CONSTRAINT uq_serial_scan_event_session_number UNIQUE (session_id, scan_number),
    CONSTRAINT fk_serial_scan_event_session
        FOREIGN KEY (session_id) REFERENCES serial_scan_sessions(id)
        ON DELETE RESTRICT ON UPDATE CASCADE,
    CONSTRAINT chk_serial_scan_event_number CHECK (scan_number >= 1),
    CONSTRAINT chk_serial_scan_event_pressure CHECK (pressure_value >= 0),
    INDEX ix_serial_scan_event_scanned_at (scanned_at),
    INDEX ix_serial_scan_event_machine (machine_id)
) ENGINE=InnoDB;
