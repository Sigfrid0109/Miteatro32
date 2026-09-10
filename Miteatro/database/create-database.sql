-- Ejecutar con una cuenta administradora de MySQL.
-- Después crea un usuario propio desde Workbench y asigna permisos
-- CREATE, REFERENCES, SELECT, INSERT, UPDATE y DELETE exclusivamente sobre miteatro.*.
CREATE DATABASE IF NOT EXISTS miteatro CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
-- La aplicación crea las tablas y relaciones en su primera conexión.

