# Integrations

Financial Manager integrates with Apple Shortcuts and Microsoft Power Platform.

- [Apple Shortcuts](ios-shortcuts/README.md): native expense request form, using Apple Mail for transport. No extra mobile app or server required.
- [Power Automate](power-automate/README.md): Outlook intake and Dataverse staging in the existing DEV environment.
- [Registrar gasto (Microsoft)](ios-shortcuts/README.md#delegated-microsoft-sign-in-variant-pending-iphone-acceptance) uses Safari and Entra PKCE; the SPA redirect is registered, but iPhone sign-in and Draft creation still require device acceptance. The HTTP transport remains a separate diagnostic.

A delivered email is not an approved expense. All financial transactions remain subject to server-side authorization, review, and posting controls.
