using System;
using System.Reflection;
using System.Windows.Forms;

namespace EasyIpSwitch
{
    internal static class LocalizationTests
    {
        private static int _assertions;

        public static int Run()
        {
            try
            {
                TranslationsSwitchImmediately();
                FlagsAreEmbedded();
                SelectorRaisesLanguageChange();
                MainFormFollowsLanguage();
                AdapterCardFollowsLanguage();
                PingViewFollowsLanguage();
                ScanViewFollowsLanguage();
            }
            finally
            {
                L.SetLanguage(AppLanguage.French, false);
            }
            return _assertions;
        }

        private static void TranslationsSwitchImmediately()
        {
            L.SetLanguage(AppLanguage.French, false);
            Assert(L.T("Actualiser") == "Actualiser", "French must be the source language.");
            Assert(L.T("Clé inconnue") == "Clé inconnue", "An unknown key must fall back to French.");

            L.SetLanguage(AppLanguage.English, false);
            Assert(L.T("Actualiser") == "Refresh", "English translation missing.");
            Assert(L.T("Cartes réseau") == "Network cards", "English network-card label missing.");
            Assert(L.F("La passerelle n'est pas dans le sous-réseau {0}.", "192.168.2.0")
                == "The gateway is not in subnet 192.168.2.0.", "English formatting must keep arguments.");

            L.SetLanguage(AppLanguage.German, false);
            Assert(L.T("Actualiser") == "Aktualisieren", "German translation missing.");
            Assert(L.T("Carte active") == "Karte aktiv", "German card label missing.");

            L.SetLanguage(AppLanguage.Spanish, false);
            Assert(L.T("Actualiser") == "Actualizar", "Spanish translation missing.");
            Assert(L.T("Carte active") == "Tarjeta activa", "Spanish card label missing.");

            L.SetLanguage(AppLanguage.French, false);
            Assert(L.DisplayName(AppLanguage.French) == "Français"
                && L.DisplayName(AppLanguage.English) == "English"
                && L.DisplayName(AppLanguage.German) == "Deutsch"
                && L.DisplayName(AppLanguage.Spanish) == "Español",
                "The language selector must use native language names.");
        }

        private static void FlagsAreEmbedded()
        {
            using (LanguageSelector selector = new LanguageSelector())
            {
                AppLanguage[] languages = new AppLanguage[]
                {
                    AppLanguage.French,
                    AppLanguage.English,
                    AppLanguage.German,
                    AppLanguage.Spanish
                };
                for (int i = 0; i < languages.Length; i++)
                {
                    System.Drawing.Image flag = selector.FlagImage(languages[i]);
                    Assert(flag != null && flag.Width == 24 && flag.Height == 16,
                        "Every language must have an embedded 24x16 flag image.");
                }
            }
        }

        private static void SelectorRaisesLanguageChange()
        {
            using (LanguageSelector selector = new LanguageSelector())
            {
                int changes = 0;
                selector.SelectedLanguageChanged += delegate { changes++; };
                selector.SelectedLanguage = AppLanguage.English;
                Assert(selector.SelectedLanguage == AppLanguage.English && changes == 1,
                    "Selecting a language must raise the change event once.");
                selector.SelectedLanguage = AppLanguage.English;
                Assert(changes == 1, "Selecting the same language must not raise another event.");
                selector.SetSelectedSilent(AppLanguage.German);
                Assert(selector.SelectedLanguage == AppLanguage.German && changes == 1,
                    "Silent selection must not raise the event.");
            }
        }

        private static void MainFormFollowsLanguage()
        {
            using (MainForm form = new MainForm())
            {
                L.SetLanguage(AppLanguage.English, false);
                form.ApplyLanguage();

                Assert(Field<FlatButton>(form, "_btnRefresh").Text == "Refresh",
                    "The refresh button must switch language without restarting.");
                Assert(Field<FlatButton>(form, "_btnShortcut").Text == "Create shortcut",
                    "The shortcut button must switch language.");
                Assert(Field<FlatButton>(form, "_btnConnections").Text == "Network connections",
                    "The connections button must switch language.");
                TabBar tabs = Field<TabBar>(form, "_tabs");
                Assert(tabs.Items.Length == 3
                    && tabs.Items[0] == "Network cards"
                    && tabs.Items[1] == "Ping test"
                    && tabs.Items[2] == "Network scan",
                    "The tabs must switch language.");
                Assert(Field<LinkLabel>(form, "_lnkDonate").Text == "Donate",
                    "The donation link must switch language.");
                Assert(Field<Label>(form, "_lblCopyright").Text.IndexOf("All rights reserved", StringComparison.Ordinal) >= 0,
                    "The copyright line must switch language.");
                LanguageSelector languages = Field<LanguageSelector>(form, "_languageSelector");
                Assert(languages.SelectedLanguage == AppLanguage.English,
                    "The selector must reflect the active language.");

                L.SetLanguage(AppLanguage.French, false);
                form.ApplyLanguage();
                Assert(Field<FlatButton>(form, "_btnRefresh").Text == "Actualiser",
                    "Switching back to French must be immediate.");
                Assert(tabs.Items[0] == "Cartes réseau", "French tabs must be restored.");
            }
        }

        private static void AdapterCardFollowsLanguage()
        {
            AdapterInfo info = new AdapterInfo();
            info.Name = "Synthetic adapter";
            info.Index = 9999;
            info.Enabled = true;
            info.EnabledKnown = true;
            info.Dhcp = false;
            info.Ip = "192.168.1.50";
            info.Mask = "255.255.255.0";
            info.Gateway = "192.168.1.1";

            using (AdapterCard card = new AdapterCard(info))
            {
                L.SetLanguage(AppLanguage.German, false);
                card.ApplyLanguage();

                Assert(Field<Label>(card, "_lblIp").Text == "IP-Adresse", "The IP label must switch language.");
                Assert(Field<Label>(card, "_lblMask").Text == "Maske", "The mask label must switch language.");
                Assert(Field<Label>(card, "_lblGateway").Text == "Gateway", "The gateway label must switch language.");
                Assert(Field<FlatButton>(card, "_btnApply").Text == "Übernehmen", "The apply button must switch language.");
                SegmentedControl mode = Field<SegmentedControl>(card, "_segment");
                Assert(mode.Items[1] == "Feste Adresse", "The static mode label must switch language.");
                Assert(Field<Label>(card, "_lblStatus").Text.IndexOf("Verbunden", StringComparison.Ordinal) >= 0
                    || Field<Label>(card, "_lblStatus").Text.IndexOf("Nicht verbunden", StringComparison.Ordinal) >= 0
                    || Field<Label>(card, "_lblStatus").Text.IndexOf("Deaktiviert", StringComparison.Ordinal) >= 0
                    || Field<Label>(card, "_lblStatus").Text.IndexOf("Status nicht verfügbar", StringComparison.Ordinal) >= 0,
                    "The card status must switch language.");
            }
        }

        private static void PingViewFollowsLanguage()
        {
            using (PingView view = new PingView())
            {
                L.SetLanguage(AppLanguage.Spanish, false);
                view.ApplyLanguage();

                Assert(Field<Label>(view, "_title").Text == "Probar una dirección IP en la red",
                    "The ping title must switch language.");
                Assert(Field<Label>(view, "_lblAdapter").Text == "Tarjeta de red (origen)",
                    "The ping source label must switch language.");
                Assert(Field<FlatButton>(view, "_btnClear").Text == "Borrar",
                    "The clear button must switch language.");
            }
        }

        private static void ScanViewFollowsLanguage()
        {
            L.SetLanguage(AppLanguage.French, false);
            using (ScanView view = new ScanView())
            {
                view.SetBounds(0, 0, 900, 600);
                ListView table = Field<ListView>(view, "_table");
                FlatButton stop = Field<FlatButton>(view, "_btnStop");
                string[] expectedColumns = new string[]
                {
                    "Nom", "Adresse IP", "DHCP", "Serveur DHCP",
                    "Pare-feu", "Agent", "Système", "Dernière réponse"
                };
                Assert(table.Columns.Count == expectedColumns.Length,
                    "The scan table must contain exactly eight columns.");
                Assert(!table.OwnerDraw,
                    "The scan table must use native drawing to avoid hover repaint glitches.");
                Assert(stop.Right <= view.ClientSize.Width - Theme.S(24),
                    "The scan controls must stay within the card after removing CSV export.");
                for (int i = 0; i < expectedColumns.Length; i++)
                {
                    Assert(table.Columns[i].Text == expectedColumns[i],
                        "The scan table columns must use the approved order.");
                }

                L.SetLanguage(AppLanguage.Spanish, false);
                view.ApplyLanguage();

                Assert(Field<Label>(view, "_title").Text == "Escanear la red local",
                    "The scan title must switch language.");
                Assert(Field<Label>(view, "_lblRange").Text == "Rango a escanear",
                    "The scan range label must switch language.");
                Assert(Field<FlatButton>(view, "_btnScan").Text == "Escanear",
                    "The scan button must switch language.");
                Assert(Field<FlatButton>(view, "_btnStop").Text == "Detener",
                    "The stop button must switch language.");
                Assert(table.Columns[0].Text == "Nombre"
                    && table.Columns[7].Text == "Última respuesta",
                    "The scan table headers must switch language.");
            }
        }

        private static T Field<T>(object instance, string name)
        {
            return (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(instance);
        }

        private static void Assert(bool condition, string message)
        {
            _assertions++;
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
