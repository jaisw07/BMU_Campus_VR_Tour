using UnityEngine;

namespace BMU.VRTour
{
    public enum NBRoomType
    {
        Classroom,
        ARVRLab,
        Lab,
        FacultyCabin
    }

    /// <summary>
    /// Marks one room of the North Block interior so rooms can be told apart (and found by the navigator / UI).
    /// Placed on a marker object at the centre of each room under "NB_Interior_Rooms"; it carries no geometry.
    /// </summary>
    public class NBRoomInfo : MonoBehaviour
    {
        [SerializeField] private NBRoomType m_Type;
        [SerializeField] private string m_DisplayName;
        [Tooltip("0 = ground floor, 1 = first level (y 2.7 m), 2 = top level (y 5.4 m).")]
        [SerializeField] private int m_Level;
        [Tooltip("Footprint in metres (X, Z).")]
        [SerializeField] private Vector2 m_Size;
        [Tooltip("Navigator room key this room is the entry point for (nb-classroom / nb-lab / nb-faculty), or empty.")]
        [SerializeField] private string m_NavigatorKey;

        public NBRoomType Type => m_Type;
        public string DisplayName => m_DisplayName;
        public int Level => m_Level;
        public Vector2 Size => m_Size;
        public string NavigatorKey => m_NavigatorKey;

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = m_Type == NBRoomType.Classroom ? Color.cyan :
                           m_Type == NBRoomType.FacultyCabin ? Color.yellow : Color.magenta;
            Gizmos.DrawWireCube(transform.position, new Vector3(m_Size.x, 2.4f, m_Size.y));
        }
    }
}
