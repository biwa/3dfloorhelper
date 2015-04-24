
#region ================== Copyright (c) 2007 Pascal vd Heiden

/*
 * Copyright (c) 2007 Pascal vd Heiden, www.codeimp.com
 * Copyright (c) 2014 Boris Iwanski
 * This program is released under GNU General Public License
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 * 
 */

#endregion

#region ================== Namespaces

using System;
using System.Collections;
using System.Drawing;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows.Forms;
using System.IO;
using System.Reflection;
using System.Linq;
using CodeImp.DoomBuilder.Windows;
using CodeImp.DoomBuilder.Data;
using CodeImp.DoomBuilder.IO;
using CodeImp.DoomBuilder.Map;
using CodeImp.DoomBuilder.Rendering;
using CodeImp.DoomBuilder.Geometry;
using CodeImp.DoomBuilder.Editing;
using CodeImp.DoomBuilder.Actions;
using CodeImp.DoomBuilder.Config;
using CodeImp.DoomBuilder.Types;
using CodeImp.DoomBuilder.BuilderModes;
using CodeImp.DoomBuilder.GZBuilder.Geometry;

#endregion

namespace CodeImp.DoomBuilder.ThreeDFloorMode
{
    public class SlopeObject
    {
        private ThreeDFloor threedfloor;
        private Vector2D position;
		private int v;

        public ThreeDFloor ThreeDFloor { get { return threedfloor; } set { threedfloor = value; } }
        public Vector2D Position { get { return position; } set { position = value; } }
		public int V { get { return v; } set { v = value; } }
    }

    [EditMode(DisplayName = "Slope Mode",
              SwitchAction = "threedslopemode",		// Action name used to switch to this mode
              ButtonImage = "SlopeModeIcon.png",	// Image resource name for the button
              ButtonOrder = int.MinValue + 501,	// Position of the button (lower is more to the left)
              ButtonGroup = "000_editing",
			  SupportedMapFormats = new[] { "UniversalMapSetIO" },
              UseByDefault = true,
              SafeStartMode = true)]

    public class SlopeMode : ClassicMode
	{
		#region ================== Constants

		#endregion

		#region ================== Variables

		// Highlighted item
        private SlopeVertex highlightedslope;
		private Association[] association = new Association[Thing.NUM_ARGS];
		private List<SlopeVertexGroup> copyslopevertexgroups;

		// Interface
		private bool editpressed;

        private List<ThreeDFloor> threedfloors;
        private List<SlopeObject> slopeobjects;
		bool dragging = false;

		private List<TextLabel> labels;
		private FlatVertex[] overlaygeometry;
		private FlatVertex[] overlaytaggedgeometry;
		private FlatVertex[] selectedsectorgeometry;
		private FlatVertex[] selectedtaggedsectorgeometry;

		private Vector2D dragstartmappos;
		private List<Vector2D> oldpositions;
		
		#endregion

		#region ================== Properties

		#endregion

		#region ================== Constructor / Disposer

		#endregion

		#region ================== Methods

		public override void OnHelp()
		{
			General.ShowHelp("e_things.html");
		}

		// Cancel mode
		public override void OnCancel()
		{
			base.OnCancel();

			// Return to this mode
			General.Editing.ChangeMode(new ThingsMode());
		}

		// Mode engages
		public override void OnEngage()
		{
            base.OnEngage();
            renderer.SetPresentation(Presentation.Things);

            // Convert geometry selection to sectors
            General.Map.Map.ConvertSelection(SelectionType.Sectors);

            // Get all 3D floors in the map
            threedfloors = BuilderPlug.GetThreeDFloors(General.Map.Map.Sectors.ToList());

			SetupLabels();

			foreach (SlopeVertexGroup svg in BuilderPlug.Me.SlopeVertexGroups)
			{
				svg.FindSectors();
			}

			// Update overlay surfaces, so that selected sectors are drawn correctly
			updateOverlaySurfaces();

			UpdateSlopeObjects();
		}

		// Mode disengages
		public override void OnDisengage()
		{
			base.OnDisengage();
			
			// Hide highlight info
			General.Interface.HideInfo();
		}

		// This redraws the display
		public override void OnRedrawDisplay()
		{
			renderer.RedrawSurface();

			// Render lines and vertices
			if(renderer.StartPlotter(true))
			{
				renderer.PlotLinedefSet(General.Map.Map.Linedefs);
				renderer.PlotVerticesSet(General.Map.Map.Vertices);

				renderer.Finish();
			}

			// Render things
			if(renderer.StartThings(true))
			{
				renderer.RenderThingSet(General.Map.ThingsFilter.HiddenThings, Presentation.THINGS_HIDDEN_ALPHA);
				renderer.RenderThingSet(General.Map.ThingsFilter.VisibleThings, 1.0f);

				renderer.Finish();
			}

            UpdateOverlay();

			renderer.Present();
		}

		private void UpdateSlopeObjects()
		{
			slopeobjects = new List<SlopeObject>();

			foreach (ThreeDFloor tdf in threedfloors)
			{
				if (!tdf.TopSloped || !tdf.BottomSloped)
					continue;

				SlopeObject so = new SlopeObject();
				so.ThreeDFloor = tdf;
				so.Position = tdf.BottomSlope.V1;
				so.V = 1;
				slopeobjects.Add(so);

				so = new SlopeObject();
				so.ThreeDFloor = tdf;
				so.Position = tdf.BottomSlope.V2;
				so.V = 2;
				slopeobjects.Add(so);

				if (!tdf.BottomSlope.IsSimple)
				{
					so = new SlopeObject();
					so.ThreeDFloor = tdf;
					so.Position = tdf.BottomSlope.V3;
					so.V = 3;
					slopeobjects.Add(so);
				}

			}
		}

		private void SetupLabels()
		{
			labels = new List<TextLabel>();
			PixelColor white = new PixelColor(255, 255, 255, 255);

			foreach(SlopeVertexGroup svg in BuilderPlug.Me.SlopeVertexGroups)
			{
				for (int i = 0; i < svg.Vertices.Count; i++)
				{
					SlopeVertex sv = svg.Vertices[i];
					float x = sv.Pos.x;
					float y = sv.Pos.y - 14 * (1 / renderer.Scale);

					TextLabel label = new TextLabel(20);
					label.TransformCoords = true;
					label.Rectangle = new RectangleF(x, y, 0.0f, 0.0f);
					label.AlignX = TextAlignmentX.Center;
					label.AlignY = TextAlignmentY.Middle;
					label.Scale = 14f;
					label.Backcolor = General.Colors.Background.WithAlpha(255);
					label.Text = "";

					// Rearrange labels if they'd be (exactly) on each other
					// TODO: do something like that also for overlapping labels
					foreach (TextLabel l in labels)
					{
						if (l.Rectangle.X == label.Rectangle.X && l.Rectangle.Y == label.Rectangle.Y)
							label.Rectangle = new RectangleF(x, l.Rectangle.Y - 14.0f * (1 / renderer.Scale), 0.0f, 0.0f);
					}

					if (svg.Vertices.Contains(highlightedslope))
						label.Color = General.Colors.Highlight.WithAlpha(255);
					else if (sv.Selected)
						label.Color = General.Colors.Selection.WithAlpha(255);
					else
						label.Color = white;

					if (svg.Ceiling)
					{
						label.Text += String.Format("C: {0}", sv.Z);

						if (svg.Floor)
							label.Text += "; ";
					}

					if (svg.Floor)
						label.Text += String.Format("F: {0}", sv.Z);

					labels.Add(label);
				}
			}
		}

		// This updates the overlay
        private void UpdateOverlay()
        {
            float size = 9 / renderer.Scale;

			SetupLabels();

            if (renderer.StartOverlay(true))
            {
				if(overlaygeometry != null)
					renderer.RenderHighlight(overlaygeometry, General.Colors.ModelWireframe.WithAlpha(64).ToInt());

				if (overlaytaggedgeometry != null)
					renderer.RenderHighlight(overlaytaggedgeometry, General.Colors.Vertices.WithAlpha(64).ToInt());

				if (selectedsectorgeometry != null)
					renderer.RenderHighlight(selectedsectorgeometry, General.Colors.Selection.WithAlpha(64).ToInt());

				List<SlopeVertex> vertices = new List<SlopeVertex>();

				// Store all slope vertices and draw the lines between them
				foreach (SlopeVertexGroup svg in BuilderPlug.Me.SlopeVertexGroups)
				{
					for (int i = 0; i < svg.Vertices.Count; i++)
					{
						vertices.Add(svg.Vertices[i]);

						if (i < svg.Vertices.Count - 1)
							renderer.RenderLine(svg.Vertices[0].Pos, svg.Vertices[i+1].Pos, 1, new PixelColor(255, 255, 255, 255), true);
					}
				}

				// Sort the slope vertex list and draw them. The sorting ensures that selected vertices are always drawn on top
				foreach(SlopeVertex sv in vertices.OrderBy(o=>o.Selected))
				{
					PixelColor c = General.Colors.Indication;
					Vector3D v = sv.Pos;

					if (sv.Selected)
						c = General.Colors.Selection;

					renderer.RenderRectangleFilled(new RectangleF(v.x - size / 2, v.y - size / 2, size, size), General.Colors.Background, true);
					renderer.RenderRectangle(new RectangleF(v.x - size / 2, v.y - size / 2, size, size), 2, c, true);
				}

				// Draw highlighted slope vertex
				if (highlightedslope != null)
				{
					renderer.RenderRectangleFilled(new RectangleF(highlightedslope.Pos.x - size / 2, highlightedslope.Pos.y - size / 2, size, size), General.Colors.Background, true);
					renderer.RenderRectangle(new RectangleF(highlightedslope.Pos.x - size / 2, highlightedslope.Pos.y - size / 2, size, size), 2, General.Colors.Highlight, true);
				}

				foreach (TextLabel l in labels)
					renderer.RenderText(l);

				if (selecting)
					RenderMultiSelection();

                renderer.Finish();
            }           
        }

		private void updateOverlaySurfaces()
		{
			string[] fieldnames = new string[] { "user_floorplane_id", "user_ceilingplane_id" };
			ICollection<Sector> orderedselection = General.Map.Map.GetSelectedSectors(true);
			List<FlatVertex> vertslist = new List<FlatVertex>();
			List<Sector> highlightedsectors = new List<Sector>();
			List<Sector> highlightedtaggedsectors = new List<Sector>();

			// Highlighted slope
			if (highlightedslope != null)
			{
				SlopeVertexGroup svg = BuilderPlug.Me.GetSlopeVertexGroup(highlightedslope);

				// All sectors the slope applies to
				foreach (Sector s in svg.Sectors)
				{
					if (s != null && !s.IsDisposed)
					{
						vertslist.AddRange(s.FlatVertices);
						highlightedsectors.Add(s);
					}
				}

				overlaygeometry = vertslist.ToArray();

				// All sectors that are tagged because of 3D floors
				vertslist = new List<FlatVertex>();

				foreach (Sector s in svg.TaggedSectors)
				{
					if (s != null && !s.IsDisposed)
					{
						vertslist.AddRange(s.FlatVertices);
						highlightedtaggedsectors.Add(s);
					}
				}

				overlaytaggedgeometry = vertslist.ToArray();
			}
			else
			{
				overlaygeometry = new FlatVertex[0];
				overlaytaggedgeometry = new FlatVertex[0];
			}

			// Selected sectors
			vertslist = new List<FlatVertex>();

			foreach (Sector s in orderedselection)
				if(!highlightedsectors.Contains(s))
					vertslist.AddRange(s.FlatVertices);

			selectedsectorgeometry = vertslist.ToArray();
		}
		
		// Selection
		protected override void OnSelectBegin()
		{
			// Item highlighted?
			if(highlightedslope != null)
			{
				// Flip selection
				highlightedslope.Selected = !highlightedslope.Selected;

				updateOverlaySurfaces();
				UpdateOverlay();
			}
			else
			{
				// Start making a selection
				StartMultiSelection();
			}

			base.OnSelectBegin();
		}

		// End selection
		protected override void OnSelectEnd()
		{
			// Not ending from a multi-selection?
			if(!selecting)
			{
				// Item highlighted?
				if (highlightedslope != null)
				{
					updateOverlaySurfaces();
					UpdateOverlay();
				}
			}

			base.OnSelectEnd();
		}

		// Start editing
		protected override void OnEditBegin()
		{
			base.OnEditBegin();
		}

		// Done editing
		protected override void OnEditEnd()
		{
			base.OnEditEnd();

			if (dragging || highlightedslope == null) return;

			SlopeVertex sv = highlightedslope;

			List<SlopeVertex> vertices = GetSelectedSlopeVertices();

			if(!vertices.Contains(highlightedslope))
				vertices.Add(highlightedslope);

			SlopeVertexEditForm svef = new SlopeVertexEditForm();
			svef.Setup(vertices);

			DialogResult result = svef.ShowDialog((Form)General.Interface);

			if (result == DialogResult.OK)
			{
				General.Map.IsChanged = true;

				BuilderPlug.Me.UpdateSlopes();
			}

			highlightedslope = null;

			updateOverlaySurfaces();
			UpdateOverlay();

			General.Interface.RedrawDisplay();
		}

		// Mouse moves
		public override void OnMouseMove(MouseEventArgs e)
		{
			base.OnMouseMove(e);

			// Not holding any buttons?
			if(e.Button == MouseButtons.None)
			{
				SlopeVertex oldhighlight = highlightedslope;

                float distance = float.MaxValue;
                float d;

				highlightedslope = null;

				foreach(SlopeVertexGroup svg in BuilderPlug.Me.SlopeVertexGroups) {
					foreach(SlopeVertex sv in svg.Vertices)
					{
						d = Vector2D.Distance(sv.Pos, mousemappos);

						if (d <= BuilderModes.BuilderPlug.Me.HighlightRange / renderer.Scale && d < distance)
						{
							distance = d;
							highlightedslope = sv;
						}
					}
				}

				if (highlightedslope != oldhighlight)
				{
					updateOverlaySurfaces();
					UpdateOverlay();
					General.Interface.RedrawDisplay();
				}
			}
			else if (dragging && highlightedslope != null)
			{
				int i = 0;

				Vector2D newpos = GridSetup.SnappedToGrid(mousemappos, General.Map.Grid.GridSizeF, 1.0f / General.Map.Grid.GridSizeF);
				Vector2D snappedstartpos = GridSetup.SnappedToGrid(dragstartmappos, General.Map.Grid.GridSizeF, 1.0f / General.Map.Grid.GridSizeF);

				foreach (SlopeVertex sl in GetSelectedSlopeVertices())
				{
					sl.Pos = oldpositions[i] + newpos - snappedstartpos;
					i++;
				}

				highlightedslope.Pos = oldpositions[i] + newpos - snappedstartpos;

				General.Map.IsChanged = true;

				updateOverlaySurfaces();
				UpdateOverlay();
				General.Interface.RedrawDisplay();
			}
			else if (selecting)
			{
				UpdateOverlay();
				General.Interface.RedrawDisplay();
			}
		}

		// Mouse leaves
		public override void OnMouseLeave(EventArgs e)
		{
			base.OnMouseLeave(e);

			// Highlight nothing
			highlightedslope = null;
		}

		// Mouse wants to drag
		protected override void OnDragStart(MouseEventArgs e)
		{
			base.OnDragStart(e);

			if (e.Button == MouseButtons.Right)
			{
				dragging = true;
				dragstartmappos = mousemappos;

				oldpositions = new List<Vector2D>();

				foreach(SlopeVertex sl in GetSelectedSlopeVertices())
					if(sl.Selected)
						oldpositions.Add(sl.Pos);


				if(highlightedslope != null)
					oldpositions.Add(highlightedslope.Pos);
			}
		}

		// Mouse wants to drag
		protected override void OnDragStop(MouseEventArgs e)
		{
			base.OnDragStop(e);

			General.Map.UndoRedo.CreateUndo("Drag slope vertex");

			BuilderPlug.Me.StoreSlopeVertexGroupsInSector();
			General.Map.Map.Update();

			BuilderPlug.Me.UpdateSlopes();

			dragging = false;
		}


		// This is called wheh selection ends
		protected override void OnEndMultiSelection()
		{
			bool selectionvolume = ((Math.Abs(base.selectionrect.Width) > 0.1f) && (Math.Abs(base.selectionrect.Height) > 0.1f));

			if(BuilderPlug.Me.AutoClearSelection && !selectionvolume)
				General.Map.Map.ClearSelectedThings();

			if(selectionvolume)
			{
				if(General.Interface.ShiftState ^ BuilderPlug.Me.AdditiveSelect)
				{
					// Go for all slope vertices
					foreach (SlopeVertex sl in GetAllSlopeVertices())
					{
						sl.Selected |= ((sl.Pos.x >= selectionrect.Left) &&
										(sl.Pos.y >= selectionrect.Top) &&
										(sl.Pos.x <= selectionrect.Right) &&
										(sl.Pos.y <= selectionrect.Bottom));
					}
				}
				else
				{
					// Go for all slope vertices
					foreach (SlopeVertex sl in GetAllSlopeVertices())
					{
						sl.Selected |= ((sl.Pos.x >= selectionrect.Left) &&
										(sl.Pos.y >= selectionrect.Top) &&
										(sl.Pos.x <= selectionrect.Right) &&
										(sl.Pos.y <= selectionrect.Bottom));
					}
				}
			}
			
			base.OnEndMultiSelection();

			// Clear overlay
			if(renderer.StartOverlay(true)) renderer.Finish();

			// Redraw
			General.Interface.RedrawDisplay();
		}

		// This is called when the selection is updated
		protected override void OnUpdateMultiSelection()
		{
			base.OnUpdateMultiSelection();

			UpdateOverlay();
		}

		public override bool OnCopyBegin()
		{
			copyslopevertexgroups = new List<SlopeVertexGroup>();

			foreach (SlopeVertexGroup svg in BuilderPlug.Me.SlopeVertexGroups)
			{
				bool copy = false;

				// Check if the current SVG has to be copied
				foreach (SlopeVertex sv in svg.Vertices)
				{
					if (sv.Selected)
					{
						copy = true;
						break;
					}
				}

				if (copy)
				{
					List<SlopeVertex> newsv = new List<SlopeVertex>();

					foreach (SlopeVertex sv in svg.Vertices)
						newsv.Add(new SlopeVertex(sv.Pos, sv.Z));

					// Use -1 for id, since a real id will be assigned when pasting
					copyslopevertexgroups.Add(new SlopeVertexGroup(-1, newsv, svg.Floor, svg.Ceiling));
				}
			}

			return true;
		}

		public override bool OnPasteBegin(PasteOptions options)
		{
			if (copyslopevertexgroups == null || copyslopevertexgroups.Count == 0)
				return false;

			// Unselect all slope vertices, so the pasted vertices can be selected
			foreach (SlopeVertexGroup svg in BuilderPlug.Me.SlopeVertexGroups)
				svg.SelectVertices(false);

			float l = copyslopevertexgroups[0].Vertices[0].Pos.x;
			float r = copyslopevertexgroups[0].Vertices[0].Pos.x;
			float t = copyslopevertexgroups[0].Vertices[0].Pos.y;
			float b = copyslopevertexgroups[0].Vertices[0].Pos.y;

			// Find the outer dimensions of all SVGs to paste
			foreach (SlopeVertexGroup svg in copyslopevertexgroups)
			{
				foreach (SlopeVertex sv in svg.Vertices)
				{
					if (sv.Pos.x < l) l = sv.Pos.x;
					if (sv.Pos.x > r) r = sv.Pos.x;
					if (sv.Pos.y > t) t = sv.Pos.y;
					if (sv.Pos.y < b) b = sv.Pos.y;
				}
			}

			Vector2D center = new Vector2D(l + ((r - l) / 2), b + ((t - b) / 2));
			Vector2D diff = center - General.Map.Grid.SnappedToGrid(mousemappos);

			foreach (SlopeVertexGroup svg in copyslopevertexgroups)
			{
				int id;
				List<SlopeVertex> newsv = new List<SlopeVertex>();

				foreach (SlopeVertex sv in svg.Vertices)
				{
					newsv.Add(new SlopeVertex(new Vector2D(sv.Pos.x - diff.x, sv.Pos.y - diff.y), sv.Z));
				}

				SlopeVertexGroup newsvg = BuilderPlug.Me.AddSlopeVertexGroup(newsv, out id, svg.Floor, svg.Ceiling);
				newsvg.SelectVertices(true);
			}

			// Redraw the display, so that pasted SVGs are shown immediately
			General.Interface.RedrawDisplay();

			// Don't go into the standard process for pasting, so tell the core that
			// pasting should not proceed
			return false;
		}

		public List<SlopeVertex> GetSelectedSlopeVertices()
		{
			List<SlopeVertex> selected = new List<SlopeVertex>();

			foreach (SlopeVertexGroup svg in BuilderPlug.Me.SlopeVertexGroups)
			{
				foreach (SlopeVertex sv in svg.Vertices)
				{
					if (sv.Selected)
						selected.Add(sv);
				}
			}

			return selected;
		}

		public List<SlopeVertex> GetAllSlopeVertices()
		{
			List<SlopeVertex> selected = new List<SlopeVertex>();

			foreach (SlopeVertexGroup svg in BuilderPlug.Me.SlopeVertexGroups)
			{
				foreach (SlopeVertex sv in svg.Vertices)
				{
					selected.Add(sv);
				}
			}

			return selected;
		}

		#endregion

		#region ================== Actions

		[BeginAction("threedflipslope")]
		public void FlipSlope()
		{
			if (highlightedslope == null)
				return;

			MessageBox.Show("Flipping temporarily removed");

			/*
			if (highlightedslope.IsOrigin)
			{
				origin = highlightedslope.ThreeDFloor.Slope.Origin + highlightedslope.ThreeDFloor.Slope.Direction;
				direction = highlightedslope.ThreeDFloor.Slope.Direction * (-1);
			}
			else 
			{
				origin = highlightedslope.ThreeDFloor.Slope.Origin + highlightedslope.ThreeDFloor.Slope.Direction;
				direction = highlightedslope.ThreeDFloor.Slope.Direction * (-1);
			}

			highlightedslope.ThreeDFloor.Slope.Origin = origin;
			highlightedslope.ThreeDFloor.Slope.Direction = direction;

			highlightedslope.ThreeDFloor.Rebuild = true;

			BuilderPlug.ProcessThreeDFloors(new List<ThreeDFloor> { highlightedslope.ThreeDFloor }, highlightedslope.ThreeDFloor.TaggedSectors);

			UpdateSlopeObjects();

			// Redraw
			General.Interface.RedrawDisplay();
			*/
		}

		// This clears the selection
		[BeginAction("clearselection", BaseAction = true)]
		public void ClearSelection()
		{
			// Clear selection
			foreach (SlopeVertexGroup svg in BuilderPlug.Me.SlopeVertexGroups)
			{
				foreach (SlopeVertex sv in svg.Vertices)
				{
					sv.Selected = false;
				}
			}
			
			// Redraw
			General.Interface.RedrawDisplay();
		}
		

		[BeginAction("deleteitem", BaseAction = true)]
		public void DeleteItem()
		{
			// Make list of selected things
			List<SlopeVertex> selected = new List<SlopeVertex>(GetSelectedSlopeVertices());

			if(highlightedslope != null)
			{
				selected.Add(highlightedslope);
			}
			
			// Anything to do?
			if(selected.Count > 0)
			{
				List<SlopeVertexGroup> groups = new List<SlopeVertexGroup>();

				General.Map.UndoRedo.CreateUndo("Delete slope");

				foreach (SlopeVertex sv in selected)
				{
					SlopeVertexGroup svg = BuilderPlug.Me.GetSlopeVertexGroup(sv);

					if (!groups.Contains(svg))
						groups.Add(svg);
				}

				foreach (SlopeVertexGroup svg in groups)
				{
					svg.RemoveFromSectors();

					BuilderPlug.Me.SlopeVertexGroups.Remove(svg);
				}				

				General.Map.IsChanged = true;

				// Invoke a new mousemove so that the highlighted item updates
				MouseEventArgs e = new MouseEventArgs(MouseButtons.None, 0, (int)mousepos.x, (int)mousepos.y, 0);
				OnMouseMove(e);

				// Redraw screen
				General.Interface.RedrawDisplay();
			}
		}
		
		#endregion
	}
}