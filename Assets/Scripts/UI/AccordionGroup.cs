using System.Collections.Generic;
using UnityEngine;

namespace SteelCity.UI
{
    /// <summary>
    /// Manages a group of AccordionSections. When one section opens,
    /// others can be auto-collapsed (accordion behavior) or multiple
    /// can stay open (if allowMultiple = true).
    /// </summary>
    public class AccordionGroup : MonoBehaviour
    {
        [Header("Config")]
        [Tooltip("If true, multiple sections can stay open simultaneously.")]
        [SerializeField] private bool allowMultiple = false;
        [Tooltip("If true, clicking an open section closes it.")]
        [SerializeField] private bool toggleOn = true;

        private readonly List<AccordionSection> sections = new();

        public void AddSection(AccordionSection section)
        {
            if (section == null) return;
            section.SetGroup(this);
            sections.Add(section);
        }

        /// <summary>Called by AccordionSection when it's toggled.</summary>
        public void OnSectionToggled(AccordionSection section, bool isExpanding)
        {
            if (!toggleOn && isExpanding && section.IsExpanded)
            {
                // Already expanded, shouldn't happen but guard
                return;
            }

            if (isExpanding && !allowMultiple)
            {
                // Collapse all others
                foreach (var s in sections)
                {
                    if (s != null && s != section && s.IsExpanded)
                        s.CollapseInstant();
                }
            }
        }
    }
}
