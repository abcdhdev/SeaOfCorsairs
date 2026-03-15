-- Local development bootstrap:
-- - creates a dedicated app role
-- - creates separate databases for services

CREATE ROLE seawars LOGIN PASSWORD 'seawars';

CREATE DATABASE authdb OWNER seawars;
CREATE DATABASE playerdb OWNER seawars;

GRANT ALL PRIVILEGES ON DATABASE authdb TO seawars;
GRANT ALL PRIVILEGES ON DATABASE playerdb TO seawars;

