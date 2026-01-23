# Progress Dialog Localization Strings

Please add these localization entries to your resource files to fully localize the progress dialog:

## English (Resource.en-US.resx)

Add these entries between `<data name="CreateSecondaryFund"...>` and `</root>`:

```xml
<data name="DatabaseUpdateProgress" xml:space="preserve">
  <value>Database Update Progress</value>
</data>
<data name="FileValidation" xml:space="preserve">
  <value>File Validation</value>
</data>
<data name="LegacyBackup" xml:space="preserve">
  <value>Legacy Backup</value>
</data>
<data name="DataAnalysis" xml:space="preserve">
  <value>Data Analysis</value>
</data>
<data name="DatabaseBackup" xml:space="preserve">
  <value>Database Backup</value>
</data>
<data name="DataImport" xml:space="preserve">
  <value>Data Import</value>
</data>
<data name="NotStarted" xml:space="preserve">
  <value>Not started</value>
</data>
<data name="Completed" xml:space="preserve">
  <value>Completed</value>
</data>
<data name="ImportWarnings" xml:space="preserve">
  <value>Import Warnings</value>
</data>
<data name="Done" xml:space="preserve">
  <value>Done</value>
</data>
<data name="UploadInProgress" xml:space="preserve">
  <value>Upload in progress...</value>
</data>
<data name="Rows" xml:space="preserve">
  <value>Rows</value>
</data>
<data name="Inserted" xml:space="preserve">
  <value>Inserted</value>
</data>
<data name="Failed" xml:space="preserve">
  <value>Failed</value>
</data>
```

## Spanish (Resource.es-US.resx)

Add these entries between `<data name="CreateSecondaryFund"...>` and `</root>`:

```xml
<data name="DatabaseUpdateProgress" xml:space="preserve">
  <value>Progreso de Actualización de Base de Datos</value>
</data>
<data name="FileValidation" xml:space="preserve">
  <value>Validación de Archivo</value>
</data>
<data name="LegacyBackup" xml:space="preserve">
  <value>Respaldo Heredado</value>
</data>
<data name="DataAnalysis" xml:space="preserve">
  <value>Análisis de Datos</value>
</data>
<data name="DatabaseBackup" xml:space="preserve">
  <value>Respaldo de Base de Datos</value>
</data>
<data name="DataImport" xml:space="preserve">
  <value>Importación de Datos</value>
</data>
<data name="NotStarted" xml:space="preserve">
  <value>No iniciado</value>
</data>
<data name="Completed" xml:space="preserve">
  <value>Completado</value>
</data>
<data name="ImportWarnings" xml:space="preserve">
  <value>Advertencias de Importación</value>
</data>
<data name="Done" xml:space="preserve">
  <value>Hecho</value>
</data>
<data name="UploadInProgress" xml:space="preserve">
  <value>Carga en progreso...</value>
</data>
<data name="Rows" xml:space="preserve">
  <value>Filas</value>
</data>
<data name="Inserted" xml:space="preserve">
  <value>Insertado</value>
</data>
<data name="Failed" xml:space="preserve">
  <value>Falló</value>
</data>
```

## Notes:

1. **"Status" and "Close"** already exist in your resource files, so they don't need to be added again.
2. The dialog now includes **fallback support**, so it will display English text even if the localization strings aren't added yet.
3. Once you add these strings to both .resx files, the dialog will be fully localized for both English and Spanish users.
4. The fallback mechanism ensures the dialog works immediately without breaking functionality.

## Progress Dialog Elements Now Localized:

✅ **Dialog Title**: "Database Update Progress"  
✅ **Step Names**: File Validation, Legacy Backup, Data Analysis, Database Backup, Data Import  
✅ **Status Labels**: Rows, Inserted, Failed, Status, Not started, Completed  
✅ **UI Elements**: Done, Close, Upload in progress, Import Warnings  
✅ **Completion Status**: Shows localized "Completed" message  

The progress dialog will now properly support both English and Spanish languages once these strings are added to your resource files!