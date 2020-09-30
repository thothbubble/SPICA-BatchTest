using OpenTK;
using OpenTK.Graphics;

using SPICA.Formats;
using SPICA.Formats.CtrH3D;
using SPICA.Formats.CtrH3D.Animation;
using SPICA.Rendering;
using SPICA.WinForms.Formats;
using SPICA.WinForms.GUI;
using SPICA.WinForms.GUI.Animation;
using SPICA.WinForms.GUI.Viewport;
using SPICA.WinForms.Properties;

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace SPICA.WinForms
{
    public partial class FrmMain : Form
    {
        #region Declarations
        private Vector2 InitialMov;
        private Vector3 MdlCenter;
        private Vector3 Translation;
        private Matrix4 Transform;

        private GLControl Viewport;
        private GridLines UIGrid;
        private AxisLines UIAxis;
        private AnimationGroup AnimGrp;
        private H3D Scene;
        private Renderer Renderer;
        private Shader Shader;

        private float Dimension;

        private bool IgnoreClicks;
        #endregion

        #region Initialization/Termination
        public FrmMain()
        {
            //We need to add the control here cause we need to call the constructor with Graphics Mode.
            //This enables the higher precision Depth Buffer and a Stencil Buffer.
            Viewport = new GLControl(new GraphicsMode(32, 24, 8), 3, 3, GraphicsContextFlags.ForwardCompatible)
            {
                Dock = DockStyle.Fill,
                Name = "Viewport",
                VSync = true
            };

            Viewport.Load += Viewport_Load;
            Viewport.Paint += Viewport_Paint;
            Viewport.MouseDown += Viewport_MouseDown;
            Viewport.MouseMove += Viewport_MouseMove;
            Viewport.MouseWheel += Viewport_MouseWheel;
            Viewport.Resize += Viewport_Resize;

            InitializeComponent();

            MainContainer.Panel1.Controls.Add(Viewport);

            TopMenu.Renderer = new ToolsRenderer(TopMenu.BackColor);
            TopIcons.Renderer = new ToolsRenderer(TopIcons.BackColor);
            SideIcons.Renderer = new ToolsRenderer(SideIcons.BackColor);
        }

        private void FrmMain_FormClosing(object sender, FormClosingEventArgs e)
        {
            Renderer.Dispose();

            Shader.Dispose();

            SaveSettings();
        }

        public void FileOpen(string[] Files, bool MergeMode)
        {
            if (!MergeMode)
            {
                Renderer.DeleteAll();

                Renderer.Lights.Add(new Light()
                {
                    Ambient = new Color4(0.1f, 0.1f, 0.1f, 1.0f),
                    Diffuse = new Color4(0.9f, 0.9f, 0.9f, 1.0f),
                    Specular0 = new Color4(0.8f, 0.8f, 0.8f, 1.0f),
                    Specular1 = new Color4(0.4f, 0.4f, 0.4f, 1.0f),
                    TwoSidedDiffuse = true,
                    Enabled = true
                });

                ResetTransforms();

                Scene = FileIO.Merge(Files, Renderer);

                TextureManager.Textures = Scene.Textures;

                ModelsList.Bind(Scene.Models);
                TexturesList.Bind(Scene.Textures);
                CamerasList.Bind(Scene.Cameras);
                LightsList.Bind(Scene.Lights);
                SklAnimsList.Bind(Scene.SkeletalAnimations);
                MatAnimsList.Bind(Scene.MaterialAnimations);
                VisAnimsList.Bind(Scene.VisibilityAnimations);
                CamAnimsList.Bind(Scene.CameraAnimations);

                Animator.Enabled = false;
                LblAnimSpeed.Text = string.Empty;
                LblAnimLoopMode.Text = string.Empty;
                AnimSeekBar.Value = 0;
                AnimSeekBar.Maximum = 0;
                AnimGrp.Frame = 0;
                AnimGrp.FramesCount = 0;

                if (Scene.Models.Count > 0)
                {
                    ModelsList.Select(0);
                }
                else
                {
                    UpdateTransforms();
                }
            }
            else
            {
                Scene = FileIO.Merge(Files, Renderer, Scene);
            }
        }

        private void FrmMain_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.Copy;
            }
        }

        private void FrmMain_DragDrop(object sender, DragEventArgs e)
        {
            string[] Files = (string[])e.Data.GetData(DataFormats.FileDrop);

            if (Files.Length > 0)
            {
                FileOpen(Files, ModifierKeys.HasFlag(Keys.Alt) && Scene != null);
                UpdateViewport();
            }
        }

        private void LoadSettings()
        {
            if (Settings.Default.RenderShowGrid) ToggleGrid();
            if (Settings.Default.RenderShowAxis) ToggleAxis();
            if (Settings.Default.UIShowSideMenu) ToggleSide();
        }

        private void SaveSettings()
        {
            Settings.Default.RenderShowGrid = MenuShowGrid.Checked;
            Settings.Default.RenderShowAxis = MenuShowAxis.Checked;
            Settings.Default.UIShowSideMenu = MenuShowSide.Checked;

            Settings.Default.Save();
        }
        #endregion

        #region Viewport events
        private void Viewport_Load(object sender, EventArgs e)
        {
            //Note: Setting up OpenGL stuff only works after the control has loaded on the Form.
            Viewport.MakeCurrent();

            Renderer = new Renderer(Viewport.Width, Viewport.Height);

            Renderer.SetBackgroundColor(Color.Gray);

            AnimGrp = new AnimationGroup(Renderer);

            Shader = new Shader();

            UIGrid = new GridLines(Renderer, Shader);
            UIAxis = new AxisLines(Renderer, Shader);

            ResetTransforms();
            UpdateTransforms();

            LoadSettings();
        }

        private void Viewport_MouseDown(object sender, MouseEventArgs e)
        {
            if (!IgnoreClicks && e.Button != 0)
            {
                InitialMov = new Vector2(e.X, e.Y);
            }
        }

        private void Viewport_MouseMove(object sender, MouseEventArgs e)
        {
            if (!IgnoreClicks && e.Button != 0)
            {
                if ((e.Button & MouseButtons.Left) != 0)
                {
                    float X = (float)(((e.X - InitialMov.X) / Width) * Math.PI);
                    float Y = (float)(((e.Y - InitialMov.Y) / Height) * Math.PI);

                    Transform.Row3.Xyz -= Translation;

                    Transform *=
                        Matrix4.CreateRotationX(Y) *
                        Matrix4.CreateRotationY(X);

                    Transform.Row3.Xyz += Translation;
                }

                if ((e.Button & MouseButtons.Right) != 0)
                {
                    float X = (InitialMov.X - e.X) * Dimension * 0.005f;
                    float Y = (InitialMov.Y - e.Y) * Dimension * 0.005f;

                    Vector3 Offset = new Vector3(-X, Y, 0);

                    Translation += Offset;

                    Transform *= Matrix4.CreateTranslation(Offset);
                }

                InitialMov = new Vector2(e.X, e.Y);

                UpdateTransforms();
            }
        }

        private void Viewport_MouseWheel(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right)
            {
                float Step = e.Delta > 0
                    ? Dimension * 0.025f
                    : -Dimension * 0.025f;

                Translation.Z += Step;

                Transform *= Matrix4.CreateTranslation(0, 0, Step);

                UpdateTransforms();
            }
        }

        private void Viewport_Paint(object sender, PaintEventArgs e)
        {
            Renderer.Clear();

            UIGrid.Render();

            foreach (int i in ModelsList.SelectedIndices)
            {
                Renderer.Models[i].Render();
            }

            UIAxis.Render();

            Viewport.SwapBuffers();
        }

        private void Viewport_Resize(object sender, EventArgs e)
        {
            Renderer?.Resize(Viewport.Width, Viewport.Height);

            UpdateViewport();
        }
        #endregion

        #region Menu items
        private void MenuOpenFile_Click(object sender, EventArgs e)
        {
            TBtnOpen_Click(sender, e);
        }

        private void MenuMergeFiles_Click(object sender, EventArgs e)
        {
            TBtnMerge_Click(sender, e);
        }

        private void MenuBatchExport_Click(object sender, EventArgs e)
        {
            new FrmExport().Show();
        }

        private void MenuShowGrid_Click(object sender, EventArgs e)
        {
            ToggleGrid();
        }

        private void MenuShowAxis_Click(object sender, EventArgs e)
        {
            ToggleAxis();
        }

        private void MenuShowSide_Click(object sender, EventArgs e)
        {
            ToggleSide();
        }
        #endregion

        #region Tool buttons and Menus
        private void TBtnOpen_Click(object sender, EventArgs e)
        {
            Open(false);
        }

        private void TBtnMerge_Click(object sender, EventArgs e)
        {
            Open(Scene != null);
        }

        private void TBtnSave_Click(object sender, EventArgs e)
        {
            BatchExport();
        }
        private void BatchExport()
        {
            //int firstPokemon = 0;
            //int lastPokemon = 0;

            string firstPokemon;
            string lastPokemon;
            string path = "";

            using (var sr = new StreamReader("Pokemon.txt"))
            {
                //firstPokemon = GetNumber(sr.ReadLine());
                //lastPokemon = GetNumber(sr.ReadLine());

                firstPokemon = sr.ReadLine();
                string[] splitFirst = firstPokemon.Split(new[] { "FirstPokemon = " }, StringSplitOptions.None);
                firstPokemon = splitFirst[1];
                lastPokemon = sr.ReadLine();
                string[] splitSecond = lastPokemon.Split(new[] { "LastPokemon  = " }, StringSplitOptions.None);
                lastPokemon = splitSecond[1];
                path = sr.ReadLine();

                Console.WriteLine(sr.ReadToEnd());
            }

            string[] dirs = Directory.GetDirectories(path);
            string[] finalDirectories = new string[dirs.Length];
            int finalIndex = 0;
            foreach (var dir in dirs)
            {
                string dirName = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar));
                finalDirectories[finalIndex] = dirName;
                finalIndex++;
            }
            //Console.WriteLine(finalDirectories);

            bool done = false;
            for (int i = 0; i < dirs.Length; i++)
            {
                if (dirs[i].Contains(firstPokemon))
                {
                    while (!dirs[i].Contains(lastPokemon))
                    {
                        FileOpen(newPokemon(dirs[i]), false);
                        Save(GetList(SklAnimsList.ItemCount()), i);
                        FileIO.Export(Scene, i);
                        i++;
                    }
                    FileOpen(newPokemon(dirs[i]), false);
                    Save(GetList(SklAnimsList.ItemCount()), i);
                    FileIO.Export(Scene, i);
                    done = true;
                }
                if (done)
                {
                    break;
                }
            }

        }
        static string[] newPokemon(string path)
        {
            string[] output = new string[9];
            for (int i = 0; i < 9; i++)
            {
                output[i] = pokemonFile(i, path);
            }
            return output;

        }
        static string pokemonFile(int n, string path)
        {
            //int length = n.ToString().Length;
            path = path.Substring(0, path.Length);
            path += "\\";
            path += n.ToString() + ".bin";
            return path;
        }
        static int[] GetList(int count)
        {
            int[] output = new int[count];
            for (int i = 0; i < count; i++)
            {
                output[i] = count - 1 - i;
            }
            return output;
        }

        int GetNumber(string input)
        {
            string output = "";
            for (int i = 0; i < input.Length; i++)
            {
                if (Char.IsDigit(input[i]))
                {
                    output += input[i];
                }
            }
            return int.Parse(output);
        }

        private void TBtnShowGrid_Click(object sender, EventArgs e)
        {
            ToggleGrid();
        }

        private void TBtnShowAxis_Click(object sender, EventArgs e)
        {
            ToggleAxis();
        }

        private void TBtnShowSide_Click(object sender, EventArgs e)
        {
            ToggleSide();
        }

        private void ToggleGrid()
        {
            bool State = !MenuShowGrid.Checked;

            MenuShowGrid.Checked = State;
            TBtnShowGrid.Checked = State;
            UIGrid.Visible = State;

            UpdateViewport();
        }

        private void ToggleAxis()
        {
            bool State = !MenuShowAxis.Checked;

            MenuShowAxis.Checked = State;
            TBtnShowAxis.Checked = State;
            UIAxis.Visible = State;

            UpdateViewport();
        }

        private void ToggleSide()
        {
            bool State = !MenuShowSide.Checked;

            MenuShowSide.Checked = State;
            TBtnShowSide.Checked = State;
            MainContainer.Panel2Collapsed = !State;
        }

        private void ToolButtonExport_Click(object sender, EventArgs e)
        {
            FileIO.Export(Scene, TexturesList.SelectedIndex, 0);
        }

        private void Open(bool MergeMode)
        {
            IgnoreClicks = true;

            using (OpenFileDialog OpenDlg = new OpenFileDialog())
            {
                OpenDlg.Filter = "All files|*.*";
                OpenDlg.Multiselect = true;

                if (OpenDlg.ShowDialog() == DialogResult.OK && OpenDlg.FileNames.Length > 0)
                {
                    FileOpen(OpenDlg.FileNames, MergeMode);
                }
            }

            //Allow app to process click from the Open dialog that goes into the Viewport.
            //This avoid the model from moving after opening a file on the dialog.
            //(Note: The problem only happens if the dialog is on top of the Viewport).
            Application.DoEvents();

            IgnoreClicks = false;

            UpdateViewport();
        }

        public void Save(int[] Animindices, int pokemon)
        {
            FileIO.Save(Scene, pokemon, new SceneState
            {
                ModelIndex = ModelsList.SelectedIndex,
                SklAnimIndices = Animindices,
                MatAnimIndices = MatAnimsList.SelectedIndex
            });
        }

        private void ResetTransforms()
        {
            MdlCenter = Vector3.Zero;

            Dimension = 100;

            Transform =
                Matrix4.CreateRotationY((float)Math.PI * 0.25f) *
                Matrix4.CreateRotationX((float)Math.PI * 0.25f) *
                Matrix4.CreateTranslation(0, 0, -200);
        }

        private void UpdateTransforms()
        {
            Renderer.Camera.ViewMatrix = Transform;

            UIAxis.Transform = Matrix4.CreateTranslation(-MdlCenter);

            UpdateViewport();
        }
        #endregion

        #region Side menu events
        private void ModelsList_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (ModelsList.SelectedIndices.Length > 0)
            {
                int Index = ModelsList.SelectedIndex;

                BoundingBox AABB = Renderer.Models[Index].GetModelAABB();

                MdlCenter = -AABB.Center;

                Dimension = 1;

                Dimension = Math.Max(Dimension, Math.Abs(AABB.Size.X));
                Dimension = Math.Max(Dimension, Math.Abs(AABB.Size.Y));
                Dimension = Math.Max(Dimension, Math.Abs(AABB.Size.Z));

                Dimension *= 2;

                Translation = new Vector3(0, 0, -Dimension);

                Transform =
                    Matrix4.CreateTranslation(MdlCenter) *
                    Matrix4.CreateTranslation(Translation);

                Renderer.Lights[0].Position.Y = AABB.Center.Y;
                Renderer.Lights[0].Position.Z = Dimension;

                foreach (int i in ModelsList.SelectedIndices)
                {
                    Renderer.Models[i].UpdateUniforms();
                }

                UpdateTransforms();
            }
        }

        private void TexturesList_SelectedIndexChanged(object sender, EventArgs e)
        {
            int Index = TexturesList.SelectedIndex;

            if (Index != -1)
            {
                TexturePreview.Image = TextureManager.GetTexture(Index);

                TextureInfo.Text = string.Format("{0} {1}x{2} {3}",
                    Scene.Textures[Index].MipmapSize,
                    Scene.Textures[Index].Width,
                    Scene.Textures[Index].Height,
                    Scene.Textures[Index].Format);
            }
            else
            {
                TexturePreview.Image = null;

                TextureInfo.Text = string.Empty;
            }
        }

        private void CamerasList_Selected(object sender, EventArgs e)
        {
            if (CamerasList.SelectedIndex != -1)
            {
                Renderer.Camera.Set(Scene.Cameras[CamerasList.SelectedIndex]);

                Transform = Renderer.Camera.ViewMatrix;
            }
            else
            {
                Renderer.Camera.Set(null);

                ResetTransforms();
            }

            UpdateViewport();
        }

        private void LightsList_Selected(object sender, EventArgs e)
        {
            if (LightsList.SelectedIndices.Length > 0)
            {
                Renderer.Lights[0].Enabled = false;
            }

            for (int i = 1; i < Renderer.Lights.Count; i++)
            {
                Renderer.Lights[i].Enabled = LightsList.SelectedIndices.Contains(i - 1);
            }

            Renderer.UpdateAllUniforms();

            UpdateViewport();
        }

        private void SklAnimsList_Selected(object sender, EventArgs e)
        {
            SetAnimation(SklAnimsList.SelectedIndices, Scene.SkeletalAnimations, AnimationType.Skeletal);
        }

        private void MatAnimsList_Selected(object sender, EventArgs e)
        {
            SetAnimation(MatAnimsList.SelectedIndices, Scene.MaterialAnimations);
        }

        private void VisAnimsList_Selected(object sender, EventArgs e)
        {
            SetAnimation(VisAnimsList.SelectedIndices, Scene.VisibilityAnimations, AnimationType.Visibility);
        }

        private void CamAnimsList_Selected(object sender, EventArgs e)
        {
            SetAnimation(CamAnimsList.SelectedIndex, Scene.CameraAnimations, AnimationType.Camera);
        }
        #endregion

        #region Animation related + playback controls
        private void Animator_Tick(object sender, EventArgs e)
        {
            AnimGrp.AdvanceFrame();

            AnimSeekBar.Value = AnimGrp.Frame;

            UpdateAnimationTransforms();

            Viewport.Invalidate();
        }

        private void UpdateAnimationTransforms()
        {
            foreach (int i in ModelsList.SelectedIndices)
            {
                Renderer.Models[i].UpdateAnimationTransforms();
            }

            if (CamAnimsList.SelectedIndex != -1)
            {
                Renderer.Camera.RecalculateMatrices();
            }
        }

        private void SetAnimation(int Index, H3DDict<H3DAnimation> SrcAnims, AnimationType Type)
        {
            if (Index != -1)
            {
                SetAnimation(new int[] { Index }, SrcAnims, Type);
            }
            else
            {
                SetAnimation(new int[0], SrcAnims, Type);
            }
        }

        private void SetAnimation(int[] Indices, H3DDict<H3DAnimation> SrcAnims, AnimationType Type)
        {
            List<H3DAnimation> Animations = new List<H3DAnimation>(Indices.Length);

            foreach (int i in Indices)
            {
                Animations.Add(SrcAnims[i]);
            }

            if (Type == AnimationType.Skeletal && Indices.Length == 1)
            {
                foreach (H3DAnimation SklAnim in Animations)
                {
                    int MIndex = Scene.MaterialAnimations.Find(SklAnim.Name);

                    if (MIndex != -1 && !MatAnimsList.SelectedIndices.Contains(MIndex))
                    {
                        MatAnimsList.Select(MIndex);
                    }
                }
            }

            AnimGrp.Frame = 0;

            AnimGrp.SetAnimations(Animations, Type);

            AnimGrp.UpdateState();

            AnimSeekBar.Value = AnimGrp.Frame;
            AnimSeekBar.Maximum = AnimGrp.FramesCount;

            UpdateAnimationTransforms();
            UpdateAnimLbls();
            UpdateViewport();
        }

        private void SetAnimation(int[] Indices, H3DDict<H3DMaterialAnim> SrcAnims)
        {
            List<H3DAnimation> Animations = new List<H3DAnimation>(Indices.Length);

            foreach (int i in Indices)
            {
                Animations.Add(SrcAnims[i]);
            }

            AnimGrp.Frame = 0;

            AnimGrp.SetAnimations(Animations, AnimationType.Material);

            AnimGrp.UpdateState();

            AnimSeekBar.Value = AnimGrp.Frame;
            AnimSeekBar.Maximum = AnimGrp.FramesCount;

            UpdateAnimationTransforms();
            UpdateAnimLbls();
            UpdateViewport();
        }

        private void UpdateAnimLbls()
        {
            LblAnimSpeed.Text = $"{Math.Abs(AnimGrp.Step).ToString("N2")}x";

            LblAnimLoopMode.Text = AnimGrp.IsLooping
                ? "LOOP"
                : "1 GO";
        }

        private void EnableAnimator()
        {
            Animator.Enabled = true;

            UpdateAnimLbls();
        }

        private void DisableAnimator()
        {
            Animator.Enabled = false;

            UpdateViewport();
        }

        private void UpdateViewport()
        {
            if (!Animator.Enabled)
            {
                Viewport.Invalidate();
            }
        }

        private void AnimButtonPlayBackward_Click(object sender, EventArgs e)
        {
            AnimGrp.Play(-Math.Abs(AnimGrp.Step));

            EnableAnimator();
        }

        private void AnimButtonPlayForward_Click(object sender, EventArgs e)
        {
            AnimGrp.Play(Math.Abs(AnimGrp.Step));

            EnableAnimator();
        }

        private void AnimButtonPause_Click(object sender, EventArgs e)
        {
            AnimGrp.Pause();

            DisableAnimator();
        }

        private void AnimButtonStop_Click(object sender, EventArgs e)
        {
            AnimGrp.Stop();

            DisableAnimator();

            UpdateAnimationTransforms();

            AnimSeekBar.Value = 0;
        }

        private void AnimButtonSlowDown_Click(object sender, EventArgs e)
        {
            AnimGrp.SlowDown();

            UpdateAnimLbls();
        }

        private void AnimButtonSpeedUp_Click(object sender, EventArgs e)
        {
            AnimGrp.SpeedUp();

            UpdateAnimLbls();
        }

        private void AnimButtonPrev_Click(object sender, EventArgs e)
        {
            if (SklAnimsList.SelectedIndex != -1)
                SklAnimsList.SelectUp();
        }

        private void AnimButtonNext_Click(object sender, EventArgs e)
        {
            if (SklAnimsList.SelectedIndex != -1)
                SklAnimsList.SelectDown();
        }

        private void AnimSeekBar_Seek(object sender, EventArgs e)
        {
            AnimGrp.Pause();

            AnimGrp.Frame = AnimSeekBar.Value;

            UpdateAnimationTransforms();
            UpdateViewport();
        }

        private void AnimSeekBar_MouseUp(object sender, MouseEventArgs e)
        {
            AnimGrp.Continue();
        }
        #endregion
    }
}
