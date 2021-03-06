﻿#region LICENCE

// /*
//         The MIT License (MIT)
// 
//         Copyright (c) 2016 Johnathon M
// 
//         Permission is hereby granted, free of charge, to any person obtaining a copy
//         of this software and associated documentation files (the "Software"), to deal
//         in the Software without restriction, including without limitation the rights
//         to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
//         copies of the Software, and to permit persons to whom the Software is
//         furnished to do so, subject to the following conditions:
// 
//         The above copyright notice and this permission notice shall be included in
//         all copies or substantial portions of the Software.
// 
//         THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
//         IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
//         FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
//         AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
//         LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
//         OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
//         THE SOFTWARE.
// 
// */

#endregion

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using TileIconifier.Controls.Shortcut;
using TileIconifier.Core.Custom;
using TileIconifier.Core.Custom.Builder;
using TileIconifier.Core.Shortcut;
using TileIconifier.Forms.CustomShortcutForms;
using TileIconifier.Forms.Shared;
using TileIconifier.Properties;
using TileIconifier.Utilities;

namespace TileIconifier.Forms.Main
{
    public partial class FrmMain : SkinnableForm
    {
        private ShortcutItemListViewItem _currentShortcutListViewItem;
        private List<ShortcutItemListViewItem> _filteredList;

        public FrmMain()
        {
            InitializeComponent();
        }

        private ShortcutItem CurrentShortcutItem => _currentShortcutListViewItem.ShortcutItem;

        protected override void ApplySkin(object sender, EventArgs e)
        {
            base.ApplySkin(sender, e);
            iconifyPanel.UpdateSkinColors(CurrentBaseSkin);
            lblBadShortcutWarning.ForeColor = CurrentBaseSkin.ErrorColor;
        }

        private void frmDropper_Load(object sender, EventArgs e)
        {
            darkSkinToolStripMenuItem.Click += SkinToolStripMenuClick;
            defaultSkinToolStripMenuItem.Click += SkinToolStripMenuClick;
            englishToolStripMenuItem.Click += LanguageToolStripMenuClick;
            russianToolStripMenuItem.Click += LanguageToolStripMenuClick;

            SetCurrentLanguage();
            CheckPowershellPinningFromConfig();

            iconifyPanel.OnIconifyPanelUpdate += (s, ev) => { UpdateFormControls(); };

            CheckForUpdates(true);
            InitializeListboxColumns();

            Show();
            StartFullUpdate();
        }

        private void btnIconify_Click(object sender, EventArgs e)
        {
            if (!iconifyPanel.DoValidation())
            {
                return;
            }

            var showForegroundColourWarning = CurrentShortcutItem.Properties.ForegroundTextColourChanged;
            var tileIconify = GenerateTileIcon();
            tileIconify.RunIconify();
            CurrentShortcutItem.Properties.CommitChanges();
            UpdateShortcut();

            if (showForegroundColourWarning)
            {
                MessageBox.Show(
                    Strings.ForegroundColourChangeExplain,
                    Strings.ForegroundColourChange, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void btnRemove_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show(Strings.ConfirmRemoveIconification, Strings.Confirm, MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            var tileDeIconify = GenerateTileIcon();
            tileDeIconify.DeIconify();
            CurrentShortcutItem.Properties.ResetParameters();
            UpdateShortcut();
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void getPinnedItemsRequiresPowershellToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (getPinnedItemsRequiresPowershellToolStripMenuItem.Checked)
            {
                if (InvokeRequired)
                {
                    Invoke(new Action(() => { UpdatePowershellPinning(false); }));
                }
                else
                {
                    UpdatePowershellPinning(false);
                }
            }
            else
            {
                if (
                    MessageBox.Show(
                        Strings.UsesPowershell,
                        Strings.Confirm, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    UpdatePowershellPinning(true);
                }
            }
            StartFullUpdate();
        }

        private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            FormUtils.ShowCenteredDialogForm<FrmAbout>(this);
        }

        private void helpToolStripMenuItem_Click(object sender, EventArgs e)
        {
            FormUtils.ShowCenteredDialogForm<FrmHelp>(this);
        }

        private void customShortcutManagerToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (var customShortcutManager = new FrmCustomShortcutManagerMain())
            {
                customShortcutManager.ShowDialog(this);
                StartFullUpdate();
                if (customShortcutManager.GotoShortcutItem != null)
                {
                    JumpToShortcutItem(customShortcutManager.GotoShortcutItem);
                }
            }
        }

        private void refreshAllToolStripMenuItem_Click(object sender, EventArgs e)
        {
            StartFullUpdate();
        }

        private void checkForUpdateToolStripMenuItem_Click(object sender, EventArgs e)
        {
            CheckForUpdates(false);
        }

        private void SkinToolStripMenuClick(object sender, EventArgs e)
        {
            var item = sender as ToolStripMenuItem;
            CheckMenuItem(skinToolStripMenuItem, item);
            UpdateSkin();
        }

        private void srtlstShortcuts_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (srtlstShortcuts.SelectedItems.Count != 1)
            {
                return;
            }

            _currentShortcutListViewItem = (ShortcutItemListViewItem) srtlstShortcuts.SelectedItems[0];
            UpdateShortcut();
        }

        private void btnBuildCustomShortcut_Click(object sender, EventArgs e)
        {
            var shortcutName =
                Path.GetFileNameWithoutExtension(CurrentShortcutItem.ShortcutFileInfo.Name).CleanInvalidFilenameChars();

            if (CurrentShortcutItem.IsTileIconifierCustomShortcut)
            {
                return;
            }

            var cloneConfirmation = new FrmCustomShortcutConfirm
            {
                ShortcutName = shortcutName
            };

            if (cloneConfirmation.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            shortcutName = cloneConfirmation.ShortcutName;

            var parameters = new GenerateCustomShortcutParams(CurrentShortcutItem.TargetFilePath, string.Empty,
                CustomShortcutGetters.CustomShortcutCurrentUserPath)
            {
                WorkingFolder = CurrentShortcutItem.ShortcutFileInfo.Directory?.FullName
            };

            var customShortcut = new OtherCustomShortcutBuilder(parameters).GenerateCustomShortcut(shortcutName);

            StartFullUpdate();

            JumpToShortcutItem(customShortcut.ShortcutItem);

            //confirm to the user the shortcut has been created
            MessageBox.Show(
                string.Format(
                    Strings.ShortcutCreatedNeedsPinning,
                    shortcutName.QuoteWrap()),
                Strings.ShortcutCreated, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void btnDeleteCustomShortcut_Click(object sender, EventArgs e)
        {
            if (
                MessageBox.Show(
                    string.Format(Strings.ConfirmDeleteCustomShortcut,
                        Path.GetFileNameWithoutExtension(CurrentShortcutItem.ShortcutFileInfo.Name).QuoteWrap()),
                    Strings.AreYouSure,
                    MessageBoxButtons.YesNo) == DialogResult.No)
            {
                return;
            }

            try
            {
                var customShortcut = CustomShortcut.Load(CurrentShortcutItem.TargetFilePath);
                customShortcut.Delete();
            }
            catch (Exception ex)
            {
                FrmException.ShowExceptionHandler(ex);
                MessageBox.Show(Strings.UnableToClearShortcuts);
            }

            StartFullUpdate();
        }

        private void txtFilter_TextChanged(object sender, EventArgs e)
        {
            UpdateFilteredList();
            BuildShortcutList();
            UpdateShortcut();
        }

        private void donateToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show(this,
                Strings.DonationNotification,
                Strings.Donation, MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation) == DialogResult.Yes)
            {
                Process.Start("https://www.paypal.me/Jonno12345");
            }
        }

        private void FrmMain_Resize(object sender, EventArgs e)
        {
            InitializeListboxColumns();
        }

        private void mnuBatchOperations_Click(object sender, EventArgs e)
        {
            FormUtils.ShowCenteredDialogForm<FrmBatchShortcut>(this);
        }
    }
}