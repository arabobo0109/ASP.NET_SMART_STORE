﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Newtonsoft.Json;
using Smartstore.Core.Catalog.Discounts;
using Smartstore.Domain;

namespace Smartstore.Core.Catalog.Categories
{
    public class CategoryMap : IEntityTypeConfiguration<Category>
    {
        public void Configure(EntityTypeBuilder<Category> builder)
        {
            builder.HasQueryFilter(c => !c.Deleted);
        }
    }

    /// <summary>
    /// Represents a category of products.
    /// </summary>
    /// TODO: (mg) (core): Implement IRulesContainer for category entity.
    [DebuggerDisplay("{Id}: {Name} (Parent: {ParentCategoryId})")]
    [Index(nameof(Deleted), Name = "IX_Deleted")]
    [Index(nameof(DisplayOrder), Name = "IX_Category_DisplayOrder")]
    [Index(nameof(LimitedToStores), Name = "IX_Category_LimitedToStores")]
    [Index(nameof(ParentCategoryId), Name = "IX_Category_ParentCategoryId")]
    [Index(nameof(SubjectToAcl), Name = "IX_Category_SubjectToAcl")]
    public partial class Category : BaseEntity, ICategoryNode, IAuditable, ISoftDeletable, IPagingOptions, IDisplayOrder
    {
        private readonly ILazyLoader _lazyLoader;

        public Category()
        {
        }

        [SuppressMessage("CodeQuality", "IDE0051:Remove unused private member.", Justification = "Required for EF lazy loading")]
        private Category(ILazyLoader lazyLoader)
        {
            _lazyLoader = lazyLoader;
        }

        /// <summary>
        /// Gets or sets the category name.
        /// </summary>
        [Required, StringLength(400)]
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the full name (category page title).
        /// </summary>
        [StringLength(400)]
        public string FullName { get; set; }

        /// <summary>
        /// Gets or sets the description.
        /// </summary>
        [MaxLength]
        public string Description { get; set; }

        /// <summary>
        /// Gets or sets a description displayed at the bottom of the category page.
        /// </summary>
        [MaxLength]
        public string BottomDescription { get; set; }

        /// <summary>
        /// Gets or sets the external link expression. If set, any category menu item will navigate to the specified link.
        /// </summary>
        [StringLength(255)]
        public string ExternalLink { get; set; }

        /// <summary>
		/// Gets or sets a text displayed in a badge next to the category within menus.
		/// </summary>
        [StringLength(400)]
        public string BadgeText { get; set; }

        /// <summary>
		/// Gets or sets the type of the badge within menus.
		/// </summary>
        public int BadgeStyle { get; set; }

        /// <summary>
        /// Gets or sets the category alias.
        /// It's an optional key intended for advanced customization.
        /// </summary>
        [StringLength(100)]
        public string Alias { get; set; }

        /// <summary>
        /// Gets or sets the category template identifier.
        /// </summary>
        public int CategoryTemplateId { get; set; }

        /// <summary>
        /// Gets or sets the meta keywords.
        /// </summary>
        [StringLength(400)]
        public string MetaKeywords { get; set; }

        /// <summary>
        /// Gets or sets the meta description.
        /// </summary>
        [StringLength(4000)]
        public string MetaDescription { get; set; }

        /// <summary>
        /// Gets or sets the meta title.
        /// </summary>
        [StringLength(400)]
        public string MetaTitle { get; set; }

        /// <summary>
        /// Gets or sets the parent category identifier.
        /// </summary>
        public int ParentCategoryId { get; set; }

        /// <summary>
        /// Gets or sets the media file identifier.
        /// </summary>
        public int? MediaFileId { get; set; }

        /// TODO: (mg) (core): Implement media file navigation property for category.

        /// <inheritdoc/>
        public int? PageSize { get; set; }

        /// <inheritdoc/>
        public bool? AllowCustomersToSelectPageSize { get; set; }

        /// <inheritdoc/>
        [StringLength(200)]
        public string PageSizeOptions { get; set; }

        /// <summary>
        /// Gets or sets the available price ranges.
        /// </summary>
        [JsonIgnore, Obsolete("Price ranges are calculated automatically since version 3.")]
        [StringLength(400)]
        public string PriceRanges { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to show the category on home page.
        /// </summary>
        public bool ShowOnHomePage { get; set; }

        /// <inheritdoc/>
        public bool LimitedToStores { get; set; }

        /// <inheritdoc/>
        public bool SubjectToAcl { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the entity is published.
        /// </summary>
        public bool Published { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the entity has been deleted.
        /// </summary>
        [JsonIgnore]
        public bool Deleted { get; set; }

        /// <summary>
        /// Gets or sets the display order.
        /// </summary>
        public int DisplayOrder { get; set; }

        /// <inheritdoc/>
        public DateTime CreatedOnUtc { get; set; }

        /// <inheritdoc/>
        public DateTime UpdatedOnUtc { get; set; }

        /// <summary>
        /// Gets or sets the default view mode.
        /// </summary>
        [MaxLength]
        public string DefaultViewMode { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this category has discounts applied.
        /// </summary>
        /// <remarks>
        /// We use this property for performance optimization:
        /// if this property is set to false, then we do not need to load AppliedDiscounts navigation property.
        /// </remarks>
        public bool HasDiscountsApplied { get; set; }

        private ICollection<Discount> _appliedDiscounts;
        /// <summary>
        /// Gets or sets the applied discounts.
        /// </summary>
        public ICollection<Discount> AppliedDiscounts
        {
            get => _lazyLoader?.Load(this, ref _appliedDiscounts) ?? (_appliedDiscounts ??= new HashSet<Discount>());
            protected set => _appliedDiscounts = value;
        }

        /// <inheritdoc/>
        public string GetDisplayName()
        {
            return Name;
        }

        /// <inheritdoc/>
        public string GetDisplayNameMemberName()
        {
            return nameof(Name);
        }
    }
}
