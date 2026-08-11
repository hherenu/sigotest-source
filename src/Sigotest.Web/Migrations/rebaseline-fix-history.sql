-- Re-baseline 2026-08-03: las 38 migraciones históricas se reemplazaron por la
-- única migración 20260803192228_Baseline (mismo esquema, sin los INSERT de
-- datos reales que traían las viejas).
--
-- Este script se corre UNA vez sobre cada base EXISTENTE (creada con las
-- migraciones viejas) ANTES de arrancar la app con esta versión del código.
-- Sin esto, Migrate() intenta aplicar el Baseline completo y falla con
-- "There is already an object named 'Obras' in the database".
--
-- Requisito: la base tiene que tener TODAS las migraciones viejas aplicadas
-- (la última era 20260731182044_CongelarItemsSinMovimientoEnCerrados). Si le
-- falta alguna, aplicarla primero desde un checkout anterior al re-baseline.
--
-- Las bases NUEVAS no necesitan nada: Migrate() les aplica el Baseline normal
-- (esquema + catálogo INDEC; sin obras ni valores — los valores se cargan por
-- /indices/importar).

IF NOT EXISTS (SELECT 1 FROM __EFMigrationsHistory WHERE MigrationId = '20260803192228_Baseline')
BEGIN
    -- Guarda del requisito de arriba: sin ella, una base restaurada de un backup
    -- anterior al 31/07 quedaría con la historia reemplazada pero el esquema
    -- incompleto EN SILENCIO (Migrate() no aplicaría nada y la app fallaría en
    -- runtime, sin pista de la causa).
    IF NOT EXISTS (SELECT 1 FROM __EFMigrationsHistory
                   WHERE MigrationId = '20260731182044_CongelarItemsSinMovimientoEnCerrados')
        THROW 50001, N'Esta base NO tiene todas las migraciones viejas aplicadas (falta 20260731182044_CongelarItemsSinMovimientoEnCerrados). Aplicarlas primero desde un checkout anterior al re-baseline y recién entonces correr este script.', 1;

    BEGIN TRAN;
    DELETE FROM __EFMigrationsHistory;
    INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion)
    VALUES ('20260803192228_Baseline', '10.0.8');
    COMMIT;
    PRINT 'Historia reemplazada por el baseline.';
END
ELSE
    PRINT 'El baseline ya estaba registrado: nada que hacer.';
