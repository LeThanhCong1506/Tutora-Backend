using System;
using System.Collections.Generic;

namespace MV.DomainLayer.Entities;

public partial class Gradelevel
{
    public int Gradelevelid { get; set; }

    public string Gradename { get; set; } = null!;

    public int Levelorder { get; set; }

    public DateTime? Createdat { get; set; }

    public virtual ICollection<Studentprofile> Studentprofiles { get; set; } = new List<Studentprofile>();

    public virtual ICollection<Tutorsubjectgradeprice> Tutorsubjectgradeprices { get; set; } = new List<Tutorsubjectgradeprice>();

    public virtual ICollection<QuestionBank> QuestionBanks { get; set; } = new List<QuestionBank>();
}
