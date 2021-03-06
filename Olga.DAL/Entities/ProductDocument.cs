﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Olga.DAL.Entities
{
    public class ProductDocument
    {
        public int Id { get; set; }

        [StringLength(200)]
        public string PathToDocument { get; set; }
        public int ProductId { get; set; }
        public Product Product { get; set; }
        public int? ApprDocsTypeId { get; set; }
        public ApprDocsType ApprDocsType { get; set; }
        public int? ArtworkId { get; set; }
        public Artwork Artwork { get; set; }
        public bool IsGtin { get; set; }
        public bool IsEan { get; set; }
        public bool IsGmp { get; set; }
    }

    public enum FileFormats
    {
        Txt, Docx, Xlsx, Ai, Pdf, Crd
    }

    [Flags]
    public enum ProductAdditionalDocsType
    {
        //Gtin,
        Ean,
        Gmp
    }

}
