CREATE TABLE IF NOT EXISTS user_lines (
    user_id INT NOT NULL,
    line_id INT NOT NULL,

    assigned_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,

    PRIMARY KEY (user_id, line_id),

    CONSTRAINT fk_user_lines_user
        FOREIGN KEY (user_id)
        REFERENCES users(id)
        ON DELETE CASCADE,

    CONSTRAINT fk_user_lines_line
        FOREIGN KEY (line_id)
        REFERENCES production_lines(id)
        ON DELETE CASCADE
);