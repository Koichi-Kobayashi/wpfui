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
    private readonly List<GroupedTabDocument> _extraGroupedDocuments = new();

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

    public ObservableCollection<GroupedTabDocument> GroupedDocuments { get; } =
    [
        new("Home", "An ungrouped, non-closable tab.", false),
        new(
            "Main.xaml",
            "Collapse Development: the selected document remains visible.",
            groupId: "development"
        ),
        new("ViewModel.cs", "Another development document.", groupId: "development"),
        new("Notes", "An ungrouped tab between groups."),
        new("README.md", "A reference document.", groupId: "reference"),
        new(
            "Guide.md",
            "The remaining Reference tab keeps its original group name and color when README.md is moved.",
            groupId: "reference"
        ),
    ];

    [ObservableProperty]
    private GroupedTabDocument? _selectedGroupedDocument;

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

    public GroupedTabDocument CreateGroupedDocument()
    {
        int number = _nextTabNumber++;
        return new GroupedTabDocument(
            $"Grouped document {number}",
            $"Content of grouped document {number}.",
            groupId: "development"
        );
    }

    [RelayCommand]
    private void AddLongTabRow()
    {
        for (int i = 0; i < 10; i++)
        {
            GroupedTabDocument document = new(
                $"Long document {_nextTabNumber++}.xaml",
                "Use the arrows or the mouse wheel over the header strip. Switch the Gallery theme to compare Light and Dark.",
                groupId: i % 2 == 0 ? "development" : "reference"
            );
            _extraGroupedDocuments.Add(document);
            GroupedDocuments.Add(document);
        }
    }

    [RelayCommand]
    private void RemoveExtraGroupedTabs()
    {
        foreach (GroupedTabDocument document in _extraGroupedDocuments)
        {
            GroupedDocuments.Remove(document);
        }

        _extraGroupedDocuments.Clear();
    }
}
