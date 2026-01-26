# Dynamic Dialog Title Localization Strings

Please add these localization entries to your resource files for the dynamic dialog titles:

## English (Resource.en-US.resx)

Add these entries before the `</root>` tag:

```xml
<data name="UpdatingDonationDataTable" xml:space="preserve">
  <value>Updating Donation Data Table</value>
</data>
<data name="UpdatingAccountingDataTable" xml:space="preserve">
  <value>Updating Accounting Data Table</value>
</data>
```

## Spanish (Resource.es-US.resx)

Add these entries before the `</root>` tag:

```xml
<data name="UpdatingDonationDataTable" xml:space="preserve">
  <value>Actualizando Tabla de Datos de Donaciones</value>
</data>
<data name="UpdatingAccountingDataTable" xml:space="preserve">
  <value>Actualizando Tabla de Datos Contables</value>
</data>
```

These strings will make the upload progress dialog show context-specific titles:
- **"Updating Donation Data Table"** when uploading donation data
- **"Updating Accounting Data Table"** when uploading accounting data
- Falls back to **"Database Update Progress"** if type is unknown