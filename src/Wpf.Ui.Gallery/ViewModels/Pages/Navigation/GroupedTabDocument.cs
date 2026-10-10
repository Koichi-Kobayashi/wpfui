// This Source Code Form is subject to the terms of the MIT License.
// If a copy of the MIT was not distributed with this file, You can obtain one at https://opensource.org/licenses/MIT.
// Copyright (C) Leszek Pomianowski and WPF UI Contributors.
// All Rights Reserved.

namespace Wpf.Ui.Gallery.ViewModels.Pages.Navigation;

/// <summary>A document with writable, observable group membership for the drag-and-drop sample.</summary>
public partial class GroupedTabDocument(
    string header,
    string content,
    bool isClosable = true,
    string? groupId = null
) : ObservableObject
{
    /// <summary>Gets the document header.</summary>
    public string Header { get; } = header;

    /// <summary>Gets the document content.</summary>
    public string Content { get; } = content;

    /// <summary>Gets a value indicating whether the document can be closed.</summary>
    public bool IsClosable { get; } = isClosable;

    [ObservableProperty]
    private string? _groupId = groupId;
}
