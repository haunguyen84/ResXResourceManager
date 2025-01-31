﻿namespace tomenglertde.ResXManager.Model
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.ComponentModel;
    using System.Diagnostics.CodeAnalysis;
    using System.Globalization;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using System.Text.RegularExpressions;
    using System.Windows.Threading;

    using AutoProperties;

    using JetBrains.Annotations;

    using PropertyChanged;

    using Throttle;

    using tomenglertde.ResXManager.Infrastructure;
    using tomenglertde.ResXManager.Model.Properties;

    using TomsToolbox.Essentials;
    using TomsToolbox.Wpf;

    /// <summary>
    /// Represents one entry in the resource table.
    /// </summary>
    public sealed class ResourceTableEntry : INotifyPropertyChanged, IDataErrorInfo
    {
        private const string InvariantKey = "@Invariant";

        [NotNull]
        private readonly Regex _duplicateKeyExpression = new Regex(@"_Duplicate\[\d+\]$");
        [NotNull]
        private readonly IDictionary<CultureKey, ResourceLanguage> _languages;

        // the key actually stored in the file, identical to Key if no error occurred.
        [NotNull]
        private string _storedKey;

        // the last validation error
        [CanBeNull]
        private string _keyValidationError;

        /// <summary>
        /// Initializes a new instance of the <see cref="ResourceTableEntry" /> class.
        /// </summary>
        /// <param name="container">The owner.</param>
        /// <param name="key">The resource key.</param>
        /// <param name="index">The original index of the resource in the file.</param>
        /// <param name="languages">The localized values.</param>
        internal ResourceTableEntry([NotNull] ResourceEntity container, [NotNull] string key, double index, [NotNull] IDictionary<CultureKey, ResourceLanguage> languages)
        {
            Container = container;
            _storedKey = key;

            Key.SetBackingField(key);
            Index.SetBackingField(index);

            _languages = languages;

            Values = new ResourceTableValues<string>(_languages, lang => lang.GetValue(Key), (lang, value) => lang.SetValue(Key, value));
            Values.ValueChanged += Values_ValueChanged;

            Comments = new ResourceTableValues<string>(_languages, lang => lang.GetComment(Key), (lang, value) => lang.SetComment(Key, value));
            Comments.ValueChanged += Comments_ValueChanged;

            FileExists = new ResourceTableValues<bool>(_languages, lang => true, (lang, value) => false);

            SnapshotValues = new ResourceTableValues<string>(_languages, lang => Snapshot?.GetValueOrDefault(lang.CultureKey)?.Text, (lang, value) => false);
            SnapshotComments = new ResourceTableValues<string>(_languages, lang => Snapshot?.GetValueOrDefault(lang.CultureKey)?.Comment, (lang, value) => false);

            ValueAnnotations = new ResourceTableValues<ICollection<string>>(_languages, GetValueAnnotations, (lang, value) => false);
            CommentAnnotations = new ResourceTableValues<ICollection<string>>(_languages, GetCommentAnnotations, (lang, value) => false);

            IsItemInvariant = new ResourceTableValues<bool>(_languages, lang => GetIsInvariant(lang.CultureKey), (lang, value) => SetIsInvariant(lang.CultureKey, value));
        }

        private void ResetTableValues()
        {
            Values.ValueChanged -= Values_ValueChanged;
            Values = new ResourceTableValues<string>(_languages, lang => lang.GetValue(Key), (lang, value) => lang.SetValue(Key, value));
            Values.ValueChanged += Values_ValueChanged;

            Comments.ValueChanged -= Comments_ValueChanged;
            Comments = new ResourceTableValues<string>(_languages, lang => lang.GetComment(Key), (lang, value) => lang.SetComment(Key, value));
            Comments.ValueChanged += Comments_ValueChanged;

            FileExists = new ResourceTableValues<bool>(_languages, lang => true, (lang, value) => false);

            SnapshotValues = new ResourceTableValues<string>(_languages, lang => Snapshot?.GetValueOrDefault(lang.CultureKey)?.Text, (lang, value) => false);
            SnapshotComments = new ResourceTableValues<string>(_languages, lang => Snapshot?.GetValueOrDefault(lang.CultureKey)?.Comment, (lang, value) => false);

            ValueAnnotations = new ResourceTableValues<ICollection<string>>(_languages, GetValueAnnotations, (lang, value) => false);
            CommentAnnotations = new ResourceTableValues<ICollection<string>>(_languages, GetCommentAnnotations, (lang, value) => false);

            IsItemInvariant = new ResourceTableValues<bool>(_languages, lang => GetIsInvariant(lang.CultureKey), (lang, value) => SetIsInvariant(lang.CultureKey, value));
        }

        internal void Update(int index)
        {
            UpdateIndex(index);

            ResetTableValues();

            Refresh();
        }

        [NotNull]
        public ResourceEntity Container { get; }

        /// <summary>
        /// Gets the key of the resource.
        /// </summary>
        [NotNull]
        // ReSharper disable once MemberCanBePrivate.Global => Implicit bound to data grid.
        public string Key { get; set; } = string.Empty;

        [UsedImplicitly] // PropertyChanged.Fody
        private void OnKeyChanged()
        {
            _keyValidationError = null;

            var value = Key;

            if (_storedKey == value)
                return;

            var resourceLanguages = _languages.Values;

            if (!resourceLanguages.All(language => language.CanEdit()))
            {
                _keyValidationError = Resources.NotAllLanguagesAreEditable;
                return;
            }

            if (resourceLanguages.Any(language => language.KeyExists(value)))
            {
                _keyValidationError = string.Format(CultureInfo.CurrentCulture, Resources.KeyAlreadyExists, value);
                return;
            }

            foreach (var language in resourceLanguages)
            {
                language.RenameKey(_storedKey, value);
            }

            ResetTableValues();

            _storedKey = value;
        }

        [NotNull]
        public ResourceLanguage NeutralLanguage => _languages.First().Value;

        /// <summary>
        /// Gets or sets the comment of the neutral language.
        /// </summary>
        [CanBeNull]
        [DependsOn(nameof(Comments))]
        public string Comment
        {
            get => NeutralLanguage.GetComment(Key) ?? string.Empty;
            set => NeutralLanguage.SetComment(Key, value);
        }

        /// <summary>
        /// Gets the localized values.
        /// </summary>
        [NotNull]
        [ItemNotNull]
        public ResourceTableValues<string> Values { get; private set; }

        /// <summary>
        /// Gets the localized comments.
        /// </summary>
        [NotNull]
        [ItemNotNull]
        public ResourceTableValues<string> Comments { get; private set; }

        [DependsOn(nameof(Snapshot))]
        [NotNull]
        [ItemNotNull]
        public ResourceTableValues<string> SnapshotValues { get; private set; }

        [DependsOn(nameof(Snapshot))]
        [NotNull]
        [ItemNotNull]
        public ResourceTableValues<string> SnapshotComments { get; private set; }

        [NotNull]
        [ItemNotNull]
        public ResourceTableValues<bool> FileExists { get; private set; }

        [DependsOn(nameof(Snapshot))]
        [NotNull]
        [ItemNotNull]
        public ResourceTableValues<ICollection<string>> ValueAnnotations { get; private set; }

        [DependsOn(nameof(Snapshot))]
        [NotNull]
        [ItemNotNull]
        public ResourceTableValues<ICollection<string>> CommentAnnotations { get; private set; }

        [NotNull]
        [ItemNotNull]
        public ICollection<CultureKey> Languages => _languages.Keys;

        [NotNull]
        [ItemNotNull]
        public ResourceTableValues<bool> IsItemInvariant { get; private set; }

        [DependsOn(nameof(Comment))]
        public bool IsInvariant
        {
            get => GetIsInvariant(CultureKey.Neutral);
            set => SetIsInvariant(CultureKey.Neutral, value);
        }

        private bool GetIsInvariant([NotNull] CultureKey culture)
        {
            return Comments.GetValue(culture)?.IndexOf(InvariantKey, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool SetIsInvariant([NotNull] CultureKey culture, bool value)
        {
            var comment = Comments.GetValue(culture);

            if (value)
            {
                if (comment?.IndexOf(InvariantKey, StringComparison.OrdinalIgnoreCase) >= 0)
                    return false;

                Comments.SetValue(culture, comment + InvariantKey);
            }
            else
            {
                Comments.SetValue(culture, comment?.Replace(InvariantKey, ""));
            }

            Refresh();
            return true;
        }

        [DependsOn(nameof(Key))]
        public bool IsDuplicateKey => _duplicateKeyExpression.Match(Key).Success;

        [ItemNotNull]
        [CanBeNull]
        public ReadOnlyCollection<CodeReference> CodeReferences { get; internal set; }

        [UsedImplicitly]
        public double Index { get; set; }

        /// <summary>
        /// Updates the index to it's actual value only, without trying to adjust the file content.
        /// </summary>
        /// <param name="value">The value.</param>
        internal void UpdateIndex(double value)
        {
            if (Math.Abs(value - Index) <= double.Epsilon)
                return;

            Index.SetBackingField(value);
            OnPropertyChanged(nameof(Index));
        }

        [UsedImplicitly] // PropertyChanged.Fody
        private void OnIndexChanged()
        {
            Container.OnIndexChanged(this);
        }

        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        [CanBeNull]
        public IDictionary<CultureKey, ResourceData> Snapshot { get; set; }

        public bool CanEdit([CanBeNull] CultureKey cultureKey)
        {
            return Container.CanEdit(cultureKey);
        }

        public void Refresh()
        {
            OnValuesChanged();
            OnCommentsChanged();
        }

        public bool HasStringFormatParameterMismatches([NotNull][ItemNotNull] IEnumerable<object> cultures)
        {
            return HasStringFormatParameterMismatches(cultures.Select(CultureKey.Parse).Select(lang => Values.GetValue(lang)));
        }

        public bool HasSnapshotDifferences([NotNull][ItemNotNull] IEnumerable<object> cultures)
        {
            return Snapshot != null && cultures.Select(CultureKey.Parse).Any(IsSnapshotDifferent);
        }

        private bool IsSnapshotDifferent([NotNull] CultureKey culture)
        {
            if (Snapshot == null)
                return false;

            var snapshotValue = Snapshot.GetValueOrDefault(culture)?.Text ?? string.Empty;
            var currentValue = Values.GetValue(culture) ?? string.Empty;

            var snapshotComment = Snapshot.GetValueOrDefault(culture)?.Comment ?? string.Empty;
            var currentComment = Comments.GetValue(culture) ?? string.Empty;

            return !string.Equals(snapshotValue, currentValue, StringComparison.Ordinal) || !string.Equals(snapshotComment, currentComment, StringComparison.Ordinal);
        }

        private void Values_ValueChanged([CanBeNull] object sender, [CanBeNull] EventArgs e)
        {
            OnValuesChanged();
        }

        [Throttled(typeof(DispatcherThrottle), (int)DispatcherPriority.Input)]
        private void OnValuesChanged()
        {
            OnPropertyChanged(nameof(Values));
            OnPropertyChanged(nameof(FileExists));
            OnPropertyChanged(nameof(ValueAnnotations));
        }

        private void Comments_ValueChanged([CanBeNull] object sender, [CanBeNull] EventArgs e)
        {
            OnCommentsChanged();
        }

        [Throttled(typeof(DispatcherThrottle), (int)DispatcherPriority.Input)]
        private void OnCommentsChanged()
        {
            OnPropertyChanged(nameof(Comment));
            OnPropertyChanged(nameof(Comments));
            OnPropertyChanged(nameof(IsInvariant));
            OnPropertyChanged(nameof(IsItemInvariant));
            OnPropertyChanged(nameof(CommentAnnotations));
        }

        [NotNull]
        [ItemNotNull]
        private ICollection<string> GetValueAnnotations([NotNull] ResourceLanguage language)
        {
            var cultureKey = language.CultureKey;

            var value = Values.GetValue(cultureKey);

            return GetStringFormatParameterMismatchAnnotations(language)
                .Concat(GetSnapshotDifferences(language, value, d => d?.Text))
                .Concat(GetInvariantMismatches(cultureKey, value))
                .ToArray();
        }

        public bool GetError([NotNull] CultureKey culture, [CanBeNull] out string errorMessage)
        {
            errorMessage = null;

            var value = Values.GetValue(culture);

            var isInvariant = IsInvariant || IsItemInvariant.GetValue(culture);

            if (string.IsNullOrEmpty(value))
            {
                if (!isInvariant)
                {
                    errorMessage = GetErrorPrefix(culture) + Resources.ResourceTableEntry_Error_MissingTranslation;
                    return true;
                }
            }
            else
            {
                if (culture == CultureKey.Neutral)
                    return false;

                if (isInvariant)
                {
                    errorMessage = GetErrorPrefix(culture) + Resources.ResourceTableEntry_Error_InvariantWithValue;
                    return true;
                }

                var neutralValue = NeutralLanguage.GetValue(Key);
                if (string.IsNullOrEmpty(neutralValue))
                    return false;

                if (HasStringFormatParameterMismatches(neutralValue, value))
                {
                    errorMessage = GetErrorPrefix(culture) + Resources.ResourceTableEntry_Error_StringFormatParameterMismatch;
                    return true;
                }
            }

            return false;
        }

        [NotNull]
        private string GetErrorPrefix([NotNull] CultureKey culture) => string.Format(CultureInfo.CurrentCulture, "{0}{1}: ", Key, culture);

        [NotNull, ItemNotNull]
        private IEnumerable<string> GetInvariantMismatches([NotNull] CultureKey culture, [CanBeNull] string value)
        {
            if (culture == CultureKey.Neutral)
                yield break;

            var isInvariant = IsInvariant || IsItemInvariant.GetValue(culture);

            if (isInvariant && !string.IsNullOrEmpty(value))
                yield return Resources.ResourceTableEntry_Error_InvariantWithValue;
        }

        [NotNull]
        [ItemNotNull]
        private ICollection<string> GetCommentAnnotations([NotNull] ResourceLanguage language)
        {
            return GetSnapshotDifferences(language, Comments.GetValue(language.CultureKey), d => d?.Comment)
                .ToArray();
        }

        [NotNull, ItemNotNull]
        private IEnumerable<string> GetSnapshotDifferences([NotNull] ResourceLanguage language, [CanBeNull] string current, [NotNull] Func<ResourceData, string> selector)
        {
            var snapshot = Snapshot;
            if (snapshot == null)
                yield break;

            var snapshotData = snapshot.GetValueOrDefault(language.CultureKey);

            var snapshotValue = selector(snapshotData) ?? string.Empty;
            if (snapshotValue.Equals(current ?? string.Empty, StringComparison.Ordinal))
                yield break;

            yield return string.Format(CultureInfo.CurrentCulture, Resources.SnapshotAnnotation, snapshotValue);
        }

        [NotNull, ItemNotNull]
        private IEnumerable<string> GetStringFormatParameterMismatchAnnotations([NotNull] ResourceLanguage language)
        {
            if (language.IsNeutralLanguage)
                yield break;

            var value = language.GetValue(Key);
            if (string.IsNullOrEmpty(value))
                yield break;

            var neutralValue = NeutralLanguage.GetValue(Key);
            if (string.IsNullOrEmpty(neutralValue))
                yield break;

            if (HasStringFormatParameterMismatches(neutralValue, value))
                yield return Resources.ResourceTableEntry_Error_StringFormatParameterMismatch;
        }

        private static bool HasStringFormatParameterMismatches([NotNull][ItemNotNull] params string[] values)
        {
            return HasStringFormatParameterMismatches((IEnumerable<string>)values);
        }

        private static bool HasStringFormatParameterMismatches([NotNull][ItemNotNull] IEnumerable<string> values)
        {
            values = values.Where(value => !string.IsNullOrEmpty(value)).ToArray();

            if (!values.Any())
                return false;

            return values.Select(GetStringFormatFlags)
                .Distinct()
                .Count() > 1;
        }

        [NotNull]
        private static readonly Regex _stringFormatParameterPattern = new Regex(@"\{(\d+)(,\d+)?(:\S+)?\}");

        private static long GetStringFormatFlags([CanBeNull] string value)
        {
            if (string.IsNullOrEmpty(value))
                return 0;

            return _stringFormatParameterPattern
                .Matches(value)
                .Cast<Match>()
                .Aggregate(0L, (a, match) => a | ParseMatch(match));
        }

        private static long ParseMatch(Match match)
        {
            // the '\d' regex also matches non-latin numbers, must use int.TryPase...
            if (!int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                return 0;

            return 1L << value;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        [NotifyPropertyChangedInvocator]
        private void OnPropertyChanged([CallerMemberName][CanBeNull] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        [CanBeNull]
        string IDataErrorInfo.this[[CanBeNull] string columnName] => columnName != nameof(Key) ? null : _keyValidationError;

        [CanBeNull]
        string IDataErrorInfo.Error => null;
    }
}