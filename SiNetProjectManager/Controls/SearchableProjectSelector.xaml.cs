using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using SiNetSQL.Models;

namespace SiNetProjectManager.Controls;

/// <summary>
/// High-performance searchable ComboBox for project selection.
/// Uses ICollectionView for efficient filtering and VirtualizingStackPanel for smooth scrolling.
/// Features: Numeric sorting by Project.Number, exclusion list for dummy projects, text highlighting.
/// </summary>
public partial class SearchableProjectSelector : UserControl
{
    #region Dependency Properties

    /// <summary>
    /// The source collection of items to display.
    /// </summary>
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource),
        typeof(IEnumerable),
        typeof(SearchableProjectSelector),
        new PropertyMetadata(null, OnItemsSourceChanged));

    /// <summary>
    /// The currently selected item. Two-way binding by default.
    /// </summary>
    public static readonly DependencyProperty SelectedItemProperty = DependencyProperty.Register(
        nameof(SelectedItem),
        typeof(object),
        typeof(SearchableProjectSelector),
        new FrameworkPropertyMetadata(
            null,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.Journal,
            OnSelectedItemChanged,
            null,
            false,
            UpdateSourceTrigger.PropertyChanged));

    /// <summary>
    /// The filter text entered by the user.
    /// </summary>
    public static readonly DependencyProperty FilterTextProperty = DependencyProperty.Register(
        nameof(FilterText),
        typeof(string),
        typeof(SearchableProjectSelector),
        new PropertyMetadata(string.Empty, OnFilterTextChanged));

    /// <summary>
    /// The property path to display in the ComboBox.
    /// </summary>
    public static readonly DependencyProperty DisplayMemberPathProperty = DependencyProperty.Register(
        nameof(DisplayMemberPath),
        typeof(string),
        typeof(SearchableProjectSelector),
        new PropertyMetadata("NameAndNumber"));

    /// <summary>
    /// Whether to sort ascending (true) or descending (false). Default is descending (newest first).
    /// </summary>
    public static readonly DependencyProperty SortAscendingProperty = DependencyProperty.Register(
        nameof(SortAscending),
        typeof(bool),
        typeof(SearchableProjectSelector),
        new PropertyMetadata(false, OnSortDirectionChanged));

    /// <summary>
    /// The property path to sort by. Default is "Number" for numeric sorting.
    /// </summary>
    public static readonly DependencyProperty SortPropertyPathProperty = DependencyProperty.Register(
        nameof(SortPropertyPath),
        typeof(string),
        typeof(SearchableProjectSelector),
        new PropertyMetadata("Number", OnSortDirectionChanged));

    /// <summary>
    /// Additional filter property paths to search (comma-separated). 
    /// Default searches NameAndNumber, Title, Place.Title (city), and Company.Title (client).
    /// Supports nested properties like "Company.Title" for deep searching.
    /// </summary>
    public static readonly DependencyProperty FilterPropertiesProperty = DependencyProperty.Register(
        nameof(FilterProperties),
        typeof(string),
        typeof(SearchableProjectSelector),
        new PropertyMetadata("NameAndNumber,Title,Place.Title,Company.Title"));

    /// <summary>
    /// List of project numbers to exclude from the dropdown (e.g., dummy projects like 0, 9999).
    /// </summary>
    public static readonly DependencyProperty ExcludedNumbersProperty = DependencyProperty.Register(
        nameof(ExcludedNumbers),
        typeof(IList<float>),
        typeof(SearchableProjectSelector),
        new PropertyMetadata(null, OnExcludedNumbersChanged));

    /// <summary>
    /// External filter predicate applied in addition to text search and exclusions.
    /// Use this to apply ViewModel-level filters (e.g., by Status, JobType, User).
    /// When this changes, the view refreshes automatically.
    /// </summary>
    public static readonly DependencyProperty ExternalFilterProperty = DependencyProperty.Register(
        nameof(ExternalFilter),
        typeof(Predicate<object>),
        typeof(SearchableProjectSelector),
        new PropertyMetadata(null, OnExternalFilterChanged));

    #endregion

    #region Properties

    public IEnumerable ItemsSource
    {
        get => (IEnumerable)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    public string FilterText
    {
        get => (string)GetValue(FilterTextProperty);
        set => SetValue(FilterTextProperty, value);
    }

    public string DisplayMemberPath
    {
        get => (string)GetValue(DisplayMemberPathProperty);
        set => SetValue(DisplayMemberPathProperty, value);
    }

    public bool SortAscending
    {
        get => (bool)GetValue(SortAscendingProperty);
        set => SetValue(SortAscendingProperty, value);
    }

    public string SortPropertyPath
    {
        get => (string)GetValue(SortPropertyPathProperty);
        set => SetValue(SortPropertyPathProperty, value);
    }

    public string FilterProperties
    {
        get => (string)GetValue(FilterPropertiesProperty);
        set => SetValue(FilterPropertiesProperty, value);
    }

    /// <summary>
    /// Gets or sets the list of project numbers to exclude from display.
    /// Example: new List&lt;float&gt; { 0, 9999 } to hide dummy projects.
    /// </summary>
    public IList<float>? ExcludedNumbers
    {
        get => (IList<float>?)GetValue(ExcludedNumbersProperty);
        set => SetValue(ExcludedNumbersProperty, value);
    }

    /// <summary>
    /// Gets or sets an external filter predicate for additional ViewModel-level filtering.
    /// The filter is applied in addition to text search and exclusion filters.
    /// </summary>
    public Predicate<object>? ExternalFilter
    {
        get => (Predicate<object>?)GetValue(ExternalFilterProperty);
        set => SetValue(ExternalFilterProperty, value);
    }

    #endregion

    #region Private Fields

    private ICollectionView? _collectionView;
    private string[]? _filterPropertyPaths;
    private HashSet<float>? _excludedNumbersSet;
    private PropertyInfo? _numberProperty;
    private bool _isSyncingToComboBox;
    private bool _isUpdatingText;
    private readonly ProjectDisplayConverter _displayConverter = new();

    #endregion

    public SearchableProjectSelector()
    {
        InitializeComponent();

        // Subscribe to text changes in the editable ComboBox
        PART_ComboBox.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
            new TextChangedEventHandler(OnComboBoxTextChanged));

        Debug.WriteLine("[SearchableProjectSelector] Control initialized");
    }

    #region Public Methods

    /// <summary>
    /// Forces a refresh of the filter. Call this when ViewModel filter criteria change
    /// and you're using ExternalFilter via a bound predicate that has been updated.
    /// </summary>
    public void RefreshFilter()
    {
        ApplyFilter();
    }

    #endregion

    #region Event Handlers

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SearchableProjectSelector control)
        {
            Debug.WriteLine($"[SearchableProjectSelector] ItemsSource changed. Has items: {e.NewValue != null}");
            control.SetupCollectionView();
        }
    }

    private static void OnFilterTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SearchableProjectSelector control && !control._isUpdatingText)
        {
            Debug.WriteLine($"[SearchableProjectSelector] FilterText changed to: '{e.NewValue}'");
            control.ApplyFilter();
        }
    }

    /// <summary>
    /// Called when SelectedItem DependencyProperty changes.
    /// This fires for BOTH external binding updates AND internal sets.
    /// </summary>
    private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SearchableProjectSelector control)
        {
            var projectId = (e.NewValue as Project)?.Id ?? -1;
            var projectName = (e.NewValue as Project)?.Title ?? "null";
            Debug.WriteLine($"[SearchableProjectSelector] SelectedItem DP changed: Id={projectId}, Name='{projectName}', IsSyncingToComboBox={control._isSyncingToComboBox}");

            // Always sync the ComboBox unless we're already doing it
            if (!control._isSyncingToComboBox)
            {
                control._isSyncingToComboBox = true;
                try
                {
                    control.PART_ComboBox.SelectedItem = e.NewValue;
                    control.UpdateDisplayText();
                    Debug.WriteLine($"[SearchableProjectSelector] Synced ComboBox.SelectedItem to DP value");
                }
                finally
                {
                    control._isSyncingToComboBox = false;
                }
            }
        }
    }

    private static void OnSortDirectionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SearchableProjectSelector control)
        {
            control.ApplySorting();
        }
    }

    private static void OnExcludedNumbersChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SearchableProjectSelector control)
        {
            control.UpdateExcludedNumbersSet();
            control.ApplyFilter();
        }
    }

    /// <summary>
    /// Called when ExternalFilter changes. Refreshes the filter to apply new predicate.
    /// </summary>
    private static void OnExternalFilterChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SearchableProjectSelector control)
        {
            var contextType = control.DataContext?.GetType().Name ?? "NoContext";
            var isNull = e.NewValue == null;
            var wasNull = e.OldValue == null;
            Debug.WriteLine($"[SearchableProjectSelector:{contextType}] ExternalFilter changed - WasNull={wasNull}, IsNull={isNull}");

            // Only refresh if the collection view exists
            if (control._collectionView == null)
            {
                Debug.WriteLine($"[SearchableProjectSelector:{contextType}] ExternalFilter changed but no CollectionView yet - skipping");
                return;
            }

            // Refresh the filter
            control.ApplyExclusionFilterOnly();
        }
    }

    /// <summary>
    /// Handles text input in the editable ComboBox.
    /// Only filters when user is actually typing (not when we programmatically set text).
    /// Detects automatic WPF text updates and prevents selection reset loop.
    /// </summary>
    private void OnComboBoxTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingText) return;

        var text = PART_ComboBox.Text;

        // Guard: If we have a selected project, check if text matches its NameAndNumber
        // This prevents the "selection reset" loop when WPF sets text after selection
        if (SelectedItem is Project selectedProject)
        {
            var nameAndNumber = selectedProject.NameAndNumber;
            if (text == nameAndNumber)
            {
                Debug.WriteLine($"[SearchableProjectSelector] Ignoring WPF auto-text (NameAndNumber): '{text}'");
                // Restore the correct display text using converter
                _isUpdatingText = true;
                try
                {
                    var displayText = _displayConverter.Convert(SelectedItem, typeof(string), null!, CultureInfo.CurrentCulture) as string 
                                      ?? string.Empty;
                    PART_ComboBox.Text = displayText;
                    FilterText = displayText;
                    Debug.WriteLine($"[SearchableProjectSelector] Restored display text: '{displayText}'");
                }
                finally
                {
                    _isUpdatingText = false;
                }
                return;
            }

            // Also ignore if text matches the current display text (no need to filter)
            var currentDisplayText = _displayConverter.Convert(SelectedItem, typeof(string), null!, CultureInfo.CurrentCulture) as string;
            if (text == currentDisplayText)
            {
                return;
            }
        }

        // User is actually typing - update FilterText which will trigger filtering
        Debug.WriteLine($"[SearchableProjectSelector] User typing, FilterText = '{text}'");
        if (FilterText != text)
        {
            FilterText = text;
        }
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Handles selection changes from the internal ComboBox.
    /// This is the CRITICAL method that updates the DP and propagates to ViewModel.
    /// </summary>
    private void PART_ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingToComboBox)
        {
            Debug.WriteLine("[SearchableProjectSelector] SelectionChanged ignored - currently syncing to ComboBox");
            return;
        }

        var comboBox = sender as ComboBox;
        var newSelection = comboBox?.SelectedItem;

        var projectId = (newSelection as Project)?.Id ?? -1;
        var projectName = (newSelection as Project)?.Title ?? "null";
        Debug.WriteLine($"[SearchableProjectSelector] PART_ComboBox_SelectionChanged: Id={projectId}, Name='{projectName}'");

        // Check if this is a real change
        if (Equals(SelectedItem, newSelection))
        {
            Debug.WriteLine("[SearchableProjectSelector] SelectionChanged - same item, skipping");
            return;
        }

        // CRITICAL: Update the DependencyProperty
        // This will propagate to the ViewModel through the binding
        Debug.WriteLine($"[SearchableProjectSelector] Setting SelectedItem DP to: Id={projectId}");
        SelectedItem = newSelection;

        // Verify the DP was set
        var verifyId = (SelectedItem as Project)?.Id ?? -1;
        Debug.WriteLine($"[SearchableProjectSelector] SelectedItem DP is now: Id={verifyId}");

        // Update the display text
        UpdateDisplayText();

        // Clear the text filter so all items are visible next time dropdown opens
        ClearTextFilter();
    }

    /// <summary>
    /// Clears the text filter while preserving the exclusion filter.
    /// Called after selection to reset the dropdown for next use.
    /// </summary>
    private void ClearTextFilter()
    {
        if (_collectionView != null)
        {
            ApplyExclusionFilterOnly();
        }
    }

    /// <summary>
    /// Updates the ComboBox text to display the selected item using the ProjectDisplayConverter.
    /// </summary>
    private void UpdateDisplayText()
    {
        _isUpdatingText = true;
        try
        {
            if (SelectedItem == null)
            {
                PART_ComboBox.Text = string.Empty;
                FilterText = string.Empty;
                Debug.WriteLine("[SearchableProjectSelector] UpdateDisplayText: cleared (null selection)");
            }
            else
            {
                var displayText = _displayConverter.Convert(SelectedItem, typeof(string), null!, CultureInfo.CurrentCulture) as string 
                                  ?? string.Empty;
                PART_ComboBox.Text = displayText;
                FilterText = displayText;
                Debug.WriteLine($"[SearchableProjectSelector] UpdateDisplayText: '{displayText}'");
            }
        }
        finally
        {
            _isUpdatingText = false;
        }
    }

    private void UpdateExcludedNumbersSet()
    {
        _excludedNumbersSet = ExcludedNumbers != null && ExcludedNumbers.Count > 0
            ? new HashSet<float>(ExcludedNumbers)
            : null;
    }

    private void SetupCollectionView()
    {
        var contextType = DataContext?.GetType().Name ?? "NoContext";

        if (ItemsSource == null)
        {
            PART_ComboBox.ItemsSource = null;
            _collectionView = null;
            _numberProperty = null;
            Debug.WriteLine($"[SearchableProjectSelector:{contextType}] SetupCollectionView: ItemsSource is null");
            return;
        }

        // Create ICollectionView for efficient filtering/sorting
        _collectionView = CollectionViewSource.GetDefaultView(ItemsSource);

        // Parse filter properties
        _filterPropertyPaths = FilterProperties?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // Cache the Number property for exclusion filtering
        var enumerator = ItemsSource.GetEnumerator();
        if (enumerator.MoveNext() && enumerator.Current != null)
        {
            var itemType = enumerator.Current.GetType();
            _numberProperty = itemType.GetProperty("Number");
        }

        // Update excluded numbers set
        UpdateExcludedNumbersSet();

        // Apply sorting (no filter initially - will be applied when user types)
        ApplySorting();

        // Only apply exclusion filter initially (not text filter)
        // This should show all items unless there are exclusions or ExternalFilter is set
        ApplyExclusionFilterOnly();

        // Bind the ComboBox to the CollectionView
        PART_ComboBox.ItemsSource = _collectionView;

        // Count items for debugging
        var count = 0;
        foreach (var _ in _collectionView) count++;
        var hasExternalFilter = ExternalFilter != null;
        Debug.WriteLine($"[SearchableProjectSelector:{contextType}] SetupCollectionView: {count} items visible (HasExternalFilter={hasExternalFilter})");

        // Sync selection and display if SelectedItem was set before ItemsSource
        if (SelectedItem != null)
        {
            _isSyncingToComboBox = true;
            try
            {
                PART_ComboBox.SelectedItem = SelectedItem;
                UpdateDisplayText();
                Debug.WriteLine($"[SearchableProjectSelector:{contextType}] SetupCollectionView: Synced existing selection");
            }
            finally
            {
                _isSyncingToComboBox = false;
            }
        }
    }

    /// <summary>
    /// Applies exclusion filter and external filter (but not text filter).
    /// Used on initial load and after selection so all valid projects remain visible.
    /// </summary>
    private void ApplyExclusionFilterOnly()
    {
        if (_collectionView == null) return;

        var contextType = DataContext?.GetType().Name ?? "NoContext";
        var hasExclusionFilter = _excludedNumbersSet != null && _excludedNumbersSet.Count > 0;
        var hasExternalFilter = ExternalFilter != null;

        Debug.WriteLine($"[SearchableProjectSelector:{contextType}] ApplyExclusionFilterOnly: HasExclusion={hasExclusionFilter}, HasExternal={hasExternalFilter}");

        if (!hasExclusionFilter && !hasExternalFilter)
        {
            _collectionView.Filter = null;
            Debug.WriteLine($"[SearchableProjectSelector:{contextType}] ApplyExclusionFilterOnly: Filter cleared (no filters)");
        }
        else
        {
            // Test the external filter on first item to debug
            if (hasExternalFilter)
            {
                var enumerator = ItemsSource?.GetEnumerator();
                if (enumerator != null && enumerator.MoveNext())
                {
                    var firstItem = enumerator.Current;
                    var result = ExternalFilter!(firstItem);
                    var projectNum = (firstItem as Project)?.Number ?? -1;
                    Debug.WriteLine($"[SearchableProjectSelector:{contextType}] ApplyExclusionFilterOnly: ExternalFilter test on project #{projectNum} = {result}");
                }
            }

            _collectionView.Filter = item =>
            {
                if (item == null) return false;
                if (IsExcludedProject(item)) return false;
                if (hasExternalFilter && !ExternalFilter!(item)) return false;
                return true;
            };
        }

        // Count visible items
        var count = 0;
        foreach (var _ in _collectionView) count++;
        Debug.WriteLine($"[SearchableProjectSelector:{contextType}] ApplyExclusionFilterOnly: {count} items visible");
    }

    /// <summary>
    /// Checks if the project should be excluded based on Number.
    /// </summary>
    private bool IsExcludedProject(object item)
    {
        if (item == null || _excludedNumbersSet == null || _numberProperty == null) 
            return false;

        var numberValue = _numberProperty.GetValue(item);
        return numberValue is float number && _excludedNumbersSet.Contains(number);
    }

    private void ApplySorting()
    {
        if (_collectionView == null) return;

        using (_collectionView.DeferRefresh())
        {
            _collectionView.SortDescriptions.Clear();

            if (!string.IsNullOrEmpty(SortPropertyPath))
            {
                // Use numeric sorting for Number property (descending = newest first)
                var direction = SortAscending ? ListSortDirection.Ascending : ListSortDirection.Descending;
                _collectionView.SortDescriptions.Add(new SortDescription(SortPropertyPath, direction));
            }
        }
    }

    private void ApplyFilter()
    {
        if (_collectionView == null) return;

        var hasTextFilter = !string.IsNullOrWhiteSpace(FilterText);
        var hasExclusionFilter = _excludedNumbersSet != null && _excludedNumbersSet.Count > 0;
        var hasExternalFilter = ExternalFilter != null;

        // Get context for debug identification
        var contextType = DataContext?.GetType().Name ?? "NoContext";
        Debug.WriteLine($"[SearchableProjectSelector:{contextType}] ApplyFilter: Text='{FilterText}', HasText={hasTextFilter}, HasExclusion={hasExclusionFilter}, HasExternal={hasExternalFilter}");

        // Check if FilterText matches the current selection's display text
        // If so, don't filter by text (user selected an item and we set the text programmatically)
        if (hasTextFilter && SelectedItem != null)
        {
            var selectedDisplayText = _displayConverter.Convert(SelectedItem, typeof(string), null!, CultureInfo.CurrentCulture) as string;
            if (string.Equals(FilterText, selectedDisplayText, StringComparison.Ordinal))
            {
                Debug.WriteLine($"[SearchableProjectSelector:{contextType}] ApplyFilter: Text matches selection - using exclusion filter only");
                // Text matches selected item - only apply exclusion and external filters
                ApplyExclusionFilterOnly();
                return;
            }
        }

        if (!hasTextFilter && !hasExclusionFilter && !hasExternalFilter)
        {
            Debug.WriteLine($"[SearchableProjectSelector:{contextType}] ApplyFilter: No filters active - showing all items");
            _collectionView.Filter = null;
        }
        else
        {
            var filterLower = hasTextFilter ? FilterText.ToLowerInvariant() : null;
            Debug.WriteLine($"[SearchableProjectSelector:{contextType}] ApplyFilter: Applying combined filter (text='{filterLower}')");
            _collectionView.Filter = item => MatchesCombinedFilter(item, filterLower);
        }

        // Log count of visible items
        var count = 0;
        foreach (var item in _collectionView) count++;
        Debug.WriteLine($"[SearchableProjectSelector:{contextType}] ApplyFilter: {count} items visible after filter");
    }

    /// <summary>
    /// Combined filter that checks text search, exclusion list, AND external filter.
    /// </summary>
    private bool MatchesCombinedFilter(object item, string? filterLower)
    {
        if (item == null) return false;

        // First check exclusion list (fast path - reject excluded items immediately)
        if (IsExcludedProject(item))
        {
            return false;
        }

        // Apply external filter if provided (e.g., ViewModel filters by Status, JobType)
        if (ExternalFilter != null && !ExternalFilter(item))
        {
            return false;
        }

        // If no text filter, item passes (already passed exclusion and external checks)
        if (string.IsNullOrEmpty(filterLower))
        {
            return true;
        }

        // Check text filter against configured properties
        return MatchesTextFilter(item, filterLower);
    }

    private bool MatchesTextFilter(object item, string filterLower)
    {
        if (_filterPropertyPaths == null || _filterPropertyPaths.Length == 0)
            return true;

        var itemType = item.GetType();

        foreach (var propertyPath in _filterPropertyPaths)
        {
            // Handle nested properties (e.g., "Company.Name")
            var value = GetNestedPropertyValue(item, itemType, propertyPath);
            if (!string.IsNullOrEmpty(value) && value.ToLowerInvariant().Contains(filterLower))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Gets property value, supporting nested paths like "Company.Name".
    /// </summary>
    private static string? GetNestedPropertyValue(object item, Type itemType, string propertyPath)
    {
        var parts = propertyPath.Split('.');
        object? currentValue = item;
        var currentType = itemType;

        foreach (var part in parts)
        {
            if (currentValue == null) return null;

            var property = currentType.GetProperty(part);
            if (property == null) return null;

            currentValue = property.GetValue(currentValue);
            if (currentValue == null) return null;

            currentType = currentValue.GetType();
        }

        return currentValue?.ToString();
    }

    #endregion
}
