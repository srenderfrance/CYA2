## Missing Localization Strings for DateRangeManager Implementation

Please add these localization entries to both Resource.en-US.resx and Resource.es-US.resx files:

### English (Resource.en-US.resx)
```xml
<!-- Date Range Management -->
<data name="CustomRange" xml:space="preserve">
  <value>Custom Range</value>
</data>

<data name="ThisMonth" xml:space="preserve">
  <value>This Month</value>
</data>

<data name="LastMonth" xml:space="preserve">
  <value>Last Month</value>
</data>

<!-- Grid Column Headers -->
<data name="RefNum" xml:space="preserve">
  <value>Ref Num</value>
</data>

<data name="Description" xml:space="preserve">
  <value>Description</value>
</data>

<!-- Donation Views -->
<data name="GivingGrid" xml:space="preserve">
  <value>Giving Grid</value>
</data>

<data name="DonationList" xml:space="preserve">
  <value>Donation List</value>
</data>

<data name="Graphs" xml:space="preserve">
  <value>Graphs</value>
</data>

<data name="ComingSoon" xml:space="preserve">
  <value>Coming Soon</value>
</data>

<data name="Unknown" xml:space="preserve">
  <value>Unknown</value>
</data>

<!-- Account Overview -->
<data name="LoadedAccounts" xml:space="preserve">
  <value>Loaded accounts</value>
</data>

<data name="CalculatingDonations" xml:space="preserve">
  <value>Calculating donations</value>
</data>

<data name="NoBalancesFound" xml:space="preserve">
  <value>No balances found</value>
</data>

<data name="CopyTable" xml:space="preserve">
  <value>Copy Table</value>
</data>

<data name="CurrentAccountBalances" xml:space="preserve">
  <value>Current Account Balances</value>
</data>

<!-- Navigation -->
<data name="DonationsNav" xml:space="preserve">
  <value>Donations</value>
</data>

<data name="UserSettings" xml:space="preserve">
  <value>User Settings</value>
</data>

<!-- Confirmation Dialogs -->
<data name="ConfirmDeleteUser" xml:space="preserve">
  <value>Are you sure you want to delete the user '{0}'? This action cannot be undone.</value>
</data>

<data name="ConfirmDeleteFund" xml:space="preserve">
  <value>Are you sure you want to delete the fund '{0}'? This action cannot be undone.</value>
</data>

<!-- Other Funds -->
<data name="OtherFunds" xml:space="preserve">
  <value>Other Funds</value>
</data>
```

### Spanish (Resource.es-US.resx)
```xml
<!-- Date Range Management -->
<data name="CustomRange" xml:space="preserve">
  <value>Rango Personalizado</value>
</data>

<data name="ThisMonth" xml:space="preserve">
  <value>Este Mes</value>
</data>

<data name="LastMonth" xml:space="preserve">
  <value>Mes Pasado</value>
</data>

<!-- Grid Column Headers -->
<data name="RefNum" xml:space="preserve">
  <value>Núm Ref</value>
</data>

<data name="Description" xml:space="preserve">
  <value>Descripción</value>
</data>

<!-- Donation Views -->
<data name="GivingGrid" xml:space="preserve">
  <value>Tabla de Donaciones</value>
</data>

<data name="DonationList" xml:space="preserve">
  <value>Lista de Donaciones</value>
</data>

<data name="Graphs" xml:space="preserve">
  <value>Gráficos</value>
</data>

<data name="ComingSoon" xml:space="preserve">
  <value>Próximamente</value>
</data>

<data name="Unknown" xml:space="preserve">
  <value>Desconocido</value>
</data>

<!-- Account Overview -->
<data name="LoadedAccounts" xml:space="preserve">
  <value>Cuentas cargadas</value>
</data>

<data name="CalculatingDonations" xml:space="preserve">
  <value>Calculando donaciones</value>
</data>

<data name="NoBalancesFound" xml:space="preserve">
  <value>No se encontraron saldos</value>
</data>

<data name="CopyTable" xml:space="preserve">
  <value>Copiar Tabla</value>
</data>

<data name="CurrentAccountBalances" xml:space="preserve">
  <value>Saldos Actuales de Cuentas</value>
</data>

<!-- Navigation -->
<data name="DonationsNav" xml:space="preserve">
  <value>Donaciones</value>
</data>

<data name="UserSettings" xml:space="preserve">
  <value>Configuración del Usuario</value>
</data>

<!-- Confirmation Dialogs -->
<data name="ConfirmDeleteUser" xml:space="preserve">
  <value>¿Está seguro de que desea eliminar el usuario '{0}'? Esta acción no se puede deshacer.</value>
</data>

<data name="ConfirmDeleteFund" xml:space="preserve">
  <value>¿Está seguro de que desea eliminar el fondo '{0}'? Esta acción no se puede deshacer.</value>
</data>

<!-- Other Funds -->
<data name="OtherFunds" xml:space="preserve">
  <value>Otros Fondos</value>
</data>
```

### Notes:
1. Make sure to add these entries to the appropriate location in your .resx files
2. **New entries for Expenses grids**: **RefNum** and **Description**
3. The new preset entries are: **ThisMonth** and **LastMonth**
4. Some strings like "Date" and "Amount" likely already exist in your resource files
5. **RefNum** translates to "Núm Ref" (short for "Número de Referencia") in Spanish
6. **Description** translates to "Descripción" in Spanish
7. All strings follow the existing naming conventions in your resource files