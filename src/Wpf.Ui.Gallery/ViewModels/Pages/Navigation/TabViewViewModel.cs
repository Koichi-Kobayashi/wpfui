// This Source Code Form is subject to the terms of the MIT License.
// If a copy of the MIT was not distributed with this file, You can obtain one at https://opensource.org/licenses/MIT.
// Copyright (C) Leszek Pomianowski and WPF UI Contributors.
// All Rights Reserved.

using System.Collections.ObjectModel;
using Wpf.Ui.Controls;

namespace Wpf.Ui.Gallery.ViewModels.Pages.Navigation;

public partial class TabViewViewModel : ViewModel
{
    private int _nextTabNumber = 3;

    public ObservableCollection<TabDocument> Documents { get; } =
    [
        new("Welcome", "This tab cannot be closed.", false),
        new("Document 2", "Drag a tab header to reorder the documents."),
    ];

    [ObservableProperty]
    private TabDocument? _selectedDocument;

    public ObservableCollection<TabGroup> Groups { get; } =
    [
        new()
        {
            Id = "development",
            Header = "Development",
            Color = System.Windows.Media.Colors.DodgerBlue,
        },
        new()
        {
            Id = "reference",
            Header = "Reference",
            Color = System.Windows.Media.Colors.MediumSeaGreen,
        },
    ];

    public ObservableCollection<TabDocument> GroupedDocuments { get; } =
    [
        new("Home", "An ungrouped, non-closable tab.", false),
        new(
            "Main.xaml",
            "Collapse Development: the selected document remains visible.",
            GroupId: "development"
        ),
        new("ViewModel.cs", "Another development document.", GroupId: "development"),
        new("Notes", "An ungrouped tab between groups."),
        new("README.md", "A reference document.", GroupId: "reference"),
    ];

    [ObservableProperty]
    private TabDocument? _selectedGroupedDocument;

    public TabViewViewModel()
    {
        SelectedDocument = Documents[0];
        SelectedGroupedDocument = GroupedDocuments[1];
    }

    public TabDocument CreateDocument()
    {
        int number = _nextTabNumber++;
        return new TabDocument($"Document {number}", $"Content of document {number}.");
    }

    public TabDocument CreateGroupedDocument()
    {
        int number = _nextTabNumber++;
        return new TabDocument(
            $"Grouped document {number}",
            $"Content of grouped document {number}.",
            GroupId: "development"
        );
    }
}
