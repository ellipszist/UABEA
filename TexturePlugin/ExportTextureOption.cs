﻿using AssetsTools.NET;
using AssetsTools.NET.Extra;
using AssetsTools.NET.Texture;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UABEAvalonia;
using UABEAvalonia.Plugins;

namespace TexturePlugin
{
    public class ExportTextureOption : UABEAPluginOption
    {
        public bool SelectionValidForPlugin(AssetsManager am, UABEAPluginAction action, List<AssetContainer> selection, out string name)
        {
            if (selection.Count > 1)
                name = "Batch export textures";
            else
                name = "Export texture";

            if (action != UABEAPluginAction.Export)
                return false;

            foreach (AssetContainer cont in selection)
            {
                if (cont.ClassId != (int)AssetClassID.Texture2D)
                    return false;
            }
            return true;
        }

        public async Task<bool> ExecutePlugin(Window win, AssetWorkspace workspace, List<AssetContainer> selection)
        {
            if (selection.Count > 1)
                return await BatchExport(win, workspace, selection);
            else
                return await SingleExport(win, workspace, selection);
        }

        public async Task<bool> BatchExport(Window win, AssetWorkspace workspace, List<AssetContainer> selection)
        {
            for (int i = 0; i < selection.Count; i++)
            {
                selection[i] = new AssetContainer(selection[i], TextureHelper.GetByteArrayTexture(workspace, selection[i]));
            }

            ExportBatchChooseTypeDialog dialog = new ExportBatchChooseTypeDialog();
            string fileType = await dialog.ShowDialog<string>(win);

            if (fileType == null || fileType == string.Empty)
                return false;

            var selectedFolders = await win.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions()
            {
                Title = "Select export directory"
            });

            string[] selectedFolderPaths = FileDialogUtils.GetOpenFolderDialogFiles(selectedFolders);
            if (selectedFolderPaths.Length == 0)
                return false;

            string dir = selectedFolderPaths[0];

            StringBuilder errorBuilder = new StringBuilder();

            foreach (AssetContainer cont in selection)
            {
                string errorAssetName = $"{Path.GetFileName(cont.FileInstance.path)}/{cont.PathId}";

                AssetTypeValueField texBaseField = cont.BaseValueField;
                TextureFile texFile = TextureFile.ReadTextureFile(texBaseField);

                //0x0 texture, usually called like Font Texture or smth
                if (texFile.m_Width == 0 && texFile.m_Height == 0)
                    continue;

                string assetName = PathUtils.ReplaceInvalidPathChars(texFile.m_Name);
                string file = Path.Combine(dir, $"{assetName}-{Path.GetFileName(cont.FileInstance.path)}-{cont.PathId}.{fileType.ToLower()}");

                //bundle resS
                if (!TextureHelper.GetResSTexture(texFile, cont.FileInstance))
                {
                    string resSName = Path.GetFileName(texFile.m_StreamData.path);
                    errorBuilder.AppendLine($"[{errorAssetName}]: resS was detected but {resSName} was not found in bundle");
                    continue;
                }

                byte[] data = TextureHelper.GetRawTextureBytes(texFile, cont.FileInstance);

                if (data == null)
                {
                    string resSName = Path.GetFileName(texFile.m_StreamData.path);
                    errorBuilder.AppendLine($"[{errorAssetName}]: resS was detected but {resSName} was not found on disk");
                    continue;
                }

                byte[] platformBlob = TextureHelper.GetPlatformBlob(texBaseField);
                uint platform = cont.FileInstance.file.Metadata.TargetPlatform;

                bool success = TextureImportExport.Export(data, file, texFile.m_Width, texFile.m_Height, (TextureFormat)texFile.m_TextureFormat, platform, platformBlob);
                if (!success)
                {
                    string texFormat = ((TextureFormat)texFile.m_TextureFormat).ToString();
                    errorBuilder.AppendLine($"[{errorAssetName}]: Failed to decode texture format {texFormat}");
                    continue;
                }
            }

            if (errorBuilder.Length > 0)
            {
                string[] firstLines = errorBuilder.ToString().Split('\n').Take(20).ToArray();
                string firstLinesStr = string.Join('\n', firstLines);
                await MessageBoxUtil.ShowDialog(win, "Some errors occurred while exporting", firstLinesStr);
            }

            return true;
        }

        public async Task<bool> SingleExport(Window win, AssetWorkspace workspace, List<AssetContainer> selection)
        {
            AssetContainer cont = selection[0];

            AssetTypeValueField texBaseField = TextureHelper.GetByteArrayTexture(workspace, cont);
            TextureFile texFile = TextureFile.ReadTextureFile(texBaseField);

            // 0x0 texture, usually called like Font Texture or smth
            if (texFile.m_Width == 0 && texFile.m_Height == 0)
            {
                await MessageBoxUtil.ShowDialog(win, "Error", $"Texture size is 0x0. Texture cannot be exported.");
                return false;
            }

            string assetName = PathUtils.ReplaceInvalidPathChars(texFile.m_Name);

            var selectedFile = await win.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions()
            {
                Title = "Save texture",
                FileTypeChoices = new List<FilePickerFileType>()
                {
                    new FilePickerFileType("PNG file") { Patterns = new List<string>() { "*.png" } },
                    new FilePickerFileType("TGA file") { Patterns = new List<string>() { "*.tga" } },
                },
                SuggestedFileName = $"{assetName}-{Path.GetFileName(cont.FileInstance.path)}-{cont.PathId}",
                DefaultExtension = "png"
            });

            string selectedFilePath = FileDialogUtils.GetSaveFileDialogFile(selectedFile);
            if (selectedFilePath == null)
                return false;

            string errorAssetName = $"{Path.GetFileName(cont.FileInstance.path)}/{cont.PathId}";

            //bundle resS
            if (!TextureHelper.GetResSTexture(texFile, cont.FileInstance))
            {
                string resSName = Path.GetFileName(texFile.m_StreamData.path);
                await MessageBoxUtil.ShowDialog(win, "Error", $"[{errorAssetName}]: resS was detected but {resSName} was not found in bundle");
                return false;
            }

            byte[] data = TextureHelper.GetRawTextureBytes(texFile, cont.FileInstance);

            if (data == null)
            {
                string resSName = Path.GetFileName(texFile.m_StreamData.path);
                await MessageBoxUtil.ShowDialog(win, "Error", $"[{errorAssetName}]: resS was detected but {resSName} was not found on disk");
                return false;
            }

            byte[] platformBlob = TextureHelper.GetPlatformBlob(texBaseField);
            uint platform = cont.FileInstance.file.Metadata.TargetPlatform;

            bool success = TextureImportExport.Export(data, selectedFilePath, texFile.m_Width, texFile.m_Height, (TextureFormat)texFile.m_TextureFormat, platform, platformBlob);
            if (!success)
            {
                string texFormat = ((TextureFormat)texFile.m_TextureFormat).ToString();
                await MessageBoxUtil.ShowDialog(win, "Error", $"[{errorAssetName}]: Failed to decode texture format {texFormat}");
            }
            return success;
        }
    }
}
