﻿using Umbraco.Cms.Api.Common.Attributes;
using Umbraco.Cms.Api.Management.ViewModels.Content;
using Umbraco.Cms.Api.Management.ViewModels.DocumentType;

namespace Umbraco.Cms.Api.Management.ViewModels.Document;

[ShortGenericSchemaName<DocumentValueModel, DocumentVariantResponseModel>("ContentForDocumentResponseModel")]
public class DocumentResponseModel : ContentResponseModelBase<DocumentValueModel, DocumentVariantResponseModel>
{
    public IEnumerable<DocumentUrlInfo> Urls { get; set; } = Enumerable.Empty<DocumentUrlInfo>();

    public ReferenceByIdModel? Template { get; set; }

    public bool IsTrashed { get; set; }

    public DocumentTypeReferenceResponseModel DocumentType { get; set; } = new();
}