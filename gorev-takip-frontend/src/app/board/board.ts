import { ChangeDetectorRef, Component, OnInit } from '@angular/core';
import { CommonModule, formatDate } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CdkDragDrop, DragDropModule, moveItemInArray } from '@angular/cdk/drag-drop';
import { TaskService, TaskItem } from '../services/task';
import { SearchService } from '../services/search';
import { TaskModal } from '../task-modal/task-modal';
import { AuthService } from '../services/auth';
import { ProjectService, Project } from '../services/project';
import { UserDirectoryService, UserSummary, resolveAvatarUrl } from '../services/user-directory';

/** Panodaki bir sütunun tanımı. Üç sütunu şablonda tek tek yazmak yerine buradan
 *  döngüyle üretiyoruz; sürükle-bırak bağlantıları da burada tanımlı. */
interface BoardColumn {
  key: string;
  label: string;
  listId: string;
  connectedTo: string[];
  color: string;
}

@Component({
  selector: 'app-board',
  imports: [CommonModule, FormsModule, RouterLink, DragDropModule, TaskModal],
  templateUrl: './board.html',
  styleUrl: './board.css'
})
export class Board implements OnInit {
  loading = true;
  errorMessage = '';

  todoTasks: TaskItem[] = [];
  inProgressTasks: TaskItem[] = [];
  doneTasks: TaskItem[] = [];

  projects: Project[] = [];
  members: UserSummary[] = [];

  // --- Filtreler ---
  searchTerm = '';
  onlyMine = false;
  projectFilter: number | null = null;
  assigneeFilter: number | null = null;
  priorityFilter: string | null = null;

  modalOpen = false;
  editingTask: TaskItem | null = null;
  modalInitialStatus = 'todo';

  /** Açıkken 30 günden eski tamamlanmış görevler de listeleniyor. */
  showArchived = false;

  /** Filtre açıkken sıralama yapılamadığında gösterilen kısa açıklama. */
  reorderHint = '';

  readonly columns: BoardColumn[] = [
    { key: 'todo', label: 'Yapılacak', listId: 'todo-list', connectedTo: ['in-progress-list', 'done-list'], color: '#94a3b8' },
    { key: 'in_progress', label: 'Devam Ediyor', listId: 'in-progress-list', connectedTo: ['todo-list', 'done-list'], color: '#6366f1' },
    { key: 'done', label: 'Tamamlandı', listId: 'done-list', connectedTo: ['todo-list', 'in-progress-list'], color: '#10b981' }
  ];

  readonly priorities = ['Critical', 'High', 'Medium', 'Low'];

  readonly priorityLabels: Record<string, string> = {
    Critical: 'Kritik',
    High: 'Yüksek',
    Medium: 'Orta',
    Low: 'Düşük'
  };

  // Öncelik ölçeği dört ayrı renge ayrıldı. Önceden "Yüksek" ve "Orta" aynı sarı
  // tonundaydı, yani dört seviyeli ölçekte iki seviye ayırt edilemiyordu.
  private static readonly PRIORITY_COLORS: Record<string, { bg: string; fg: string; dot: string }> = {
    Critical: { bg: '#fee2e2', fg: '#b91c1c', dot: '#dc2626' },
    High: { bg: '#fff1e7', fg: '#c2410c', dot: '#ea580c' },
    Medium: { bg: '#eff6ff', fg: '#1d4ed8', dot: '#2563eb' },
    Low: { bg: '#f1f5f9', fg: '#475569', dot: '#64748b' }
  };

  private static readonly AVATAR_COLORS = ['#6366f1', '#0ea5e9', '#f59e0b', '#10b981', '#ec4899', '#8b5cf6'];

  private static readonly PROJECT_COLORS = [
    { bg: '#eef2ff', fg: '#4338ca' },
    { bg: '#fef3c7', fg: '#92400e' },
    { bg: '#dcfce7', fg: '#166534' },
    { bg: '#fce7f3', fg: '#9d174d' },
    { bg: '#e0f2fe', fg: '#075985' },
    { bg: '#f3e8ff', fg: '#6b21a8' }
  ];

  constructor(
    private taskService: TaskService,
    private searchService: SearchService,
    private projectService: ProjectService,
    private userDirectory: UserDirectoryService,
    public authService: AuthService,
    private cdr: ChangeDetectorRef
  ) {
    this.searchService.term$.pipe(takeUntilDestroyed()).subscribe(term => {
      this.searchTerm = term;
      this.cdr.markForCheck();
    });
  }

  ngOnInit(): void {
    this.loadTasks();
    this.loadFilterSources();
  }

  // ---------- Veri ----------

  loadTasks(): void {
    this.loading = true;
    this.errorMessage = '';
    this.taskService.getTasks(this.showArchived).subscribe({
      next: (tasks) => {
        // Sunucu görevleri SortOrder'a göre sıralı döndürüyor; filtreleme sırayı korur.
        this.todoTasks = tasks.filter(t => t.status === 'todo');
        this.inProgressTasks = tasks.filter(t => t.status === 'in_progress');
        this.doneTasks = tasks.filter(t => t.status === 'done');
        this.loading = false;
        this.cdr.markForCheck();
      },
      error: () => {
        this.loading = false;
        this.errorMessage = 'Görevler yüklenirken bir hata oluştu.';
        this.cdr.markForCheck();
      }
    });
  }

  /** Filtre açılır listelerinin kaynağı. Başarısız olursa pano yine çalışır,
   *  sadece ilgili filtre boş kalır - bu yüzden hata mesajı göstermiyoruz. */
  private loadFilterSources(): void {
    this.projectService.getProjects().subscribe({
      next: (projects) => { this.projects = projects; this.cdr.markForCheck(); },
      error: () => { this.projects = []; }
    });

    this.userDirectory.getUsers().subscribe({
      next: (users) => { this.members = users; this.cdr.markForCheck(); },
      error: () => { this.members = []; }
    });
  }

  // ---------- Sütunlar ----------

  tasksFor(key: string): TaskItem[] {
    if (key === 'in_progress') return this.inProgressTasks;
    if (key === 'done') return this.doneTasks;
    return this.todoTasks;
  }

  /** Sütuna bağlanan liste. Filtre uygulanmış diziyi veriyoruz ki sürüklenen kartın
   *  sırası ile dizideki sıra birbirini tutsun. */
  visibleFor(key: string): TaskItem[] {
    return this.tasksFor(key).filter(t => this.matches(t));
  }

  get totalTasks(): number {
    return this.todoTasks.length + this.inProgressTasks.length + this.doneTasks.length;
  }

  get completionRate(): number {
    return this.totalTasks === 0 ? 0 : Math.round((this.doneTasks.length / this.totalTasks) * 100);
  }

  // ---------- Filtreleme ----------

  private matches(task: TaskItem): boolean {
    const term = this.searchTerm.trim().toLowerCase();
    if (term) {
      const haystack = `${task.title} ${task.description || ''} ${task.projectName || ''}`.toLowerCase();
      if (!haystack.includes(term)) return false;
    }

    if (this.onlyMine && task.assignedToId !== this.authService.getUserId()) return false;
    if (this.projectFilter !== null && task.projectId !== this.projectFilter) return false;
    if (this.assigneeFilter !== null && task.assignedToId !== this.assigneeFilter) return false;
    if (this.priorityFilter !== null && task.priority !== this.priorityFilter) return false;

    return true;
  }

  get hasActiveFilters(): boolean {
    return this.onlyMine
      || this.projectFilter !== null
      || this.assigneeFilter !== null
      || this.priorityFilter !== null
      || this.searchTerm.trim() !== '';
  }

  get visibleTotal(): number {
    return this.columns.reduce((sum, c) => sum + this.visibleFor(c.key).length, 0);
  }

  toggleOnlyMine(): void {
    this.onlyMine = !this.onlyMine;
    this.cdr.markForCheck();
  }

  toggleArchived(): void {
    this.showArchived = !this.showArchived;
    this.loadTasks();
  }

  clearFilters(): void {
    this.onlyMine = false;
    this.projectFilter = null;
    this.assigneeFilter = null;
    this.priorityFilter = null;
    this.searchService.clear();
    this.cdr.markForCheck();
  }

  onFilterChange(): void {
    this.cdr.markForCheck();
  }

  // ---------- Kart görünümü ----------

  priorityLabel(priority: string): string {
    return this.priorityLabels[priority] ?? priority;
  }

  priorityStyle(priority: string): { bg: string; fg: string; dot: string } {
    return Board.PRIORITY_COLORS[priority] ?? Board.PRIORITY_COLORS['Low'];
  }

  projectStyle(projectId: number): { bg: string; fg: string } {
    return Board.PROJECT_COLORS[Math.abs(projectId) % Board.PROJECT_COLORS.length];
  }

  avatarColor(userId: number | null | undefined): string {
    if (userId === null || userId === undefined) return '#94a3b8';
    return Board.AVATAR_COLORS[Math.abs(userId) % Board.AVATAR_COLORS.length];
  }

  initials(fullName: string | null | undefined): string {
    if (!fullName) return '?';
    return fullName
      .split(' ')
      .filter(Boolean)
      .slice(0, 2)
      .map(part => part[0]?.toUpperCase())
      .join('');
  }

  /** Atanan kişinin yüklenmiş profil fotoğrafı varsa onu kullanıyoruz, yoksa baş
   *  harflerinden oluşan renkli daireye düşüyoruz. */
  assigneeAvatar(task: TaskItem): string | null {
    if (task.assignedToId === null || task.assignedToId === undefined) return null;
    const member = this.members.find(m => m.id === task.assignedToId);
    return resolveAvatarUrl(member?.avatarUrl);
  }

  private startOfDay(value: Date): Date {
    const d = new Date(value);
    d.setHours(0, 0, 0, 0);
    return d;
  }

  private daysUntilDue(task: TaskItem): number | null {
    if (!task.dueDate) return null;
    const due = this.startOfDay(new Date(task.dueDate)).getTime();
    const today = this.startOfDay(new Date()).getTime();
    return Math.round((due - today) / 86400000);
  }

  /** Yakın tarihler için gün adı yerine "Bugün / Yarın / Dün" yazıyoruz; bu üç durum
   *  tarihi okuyup hesaplamaktan çok daha hızlı anlaşılıyor. */
  dueLabel(task: TaskItem): string {
    const diff = this.daysUntilDue(task);
    if (diff === null) return '';
    if (diff === 0) return 'Bugün';
    if (diff === 1) return 'Yarın';
    if (diff === -1) return 'Dün';
    return formatDate(task.dueDate!, 'd MMM', 'tr');
  }

  isOverdue(task: TaskItem): boolean {
    if (task.status === 'done') return false;
    const diff = this.daysUntilDue(task);
    return diff !== null && diff < 0;
  }

  isDueSoon(task: TaskItem): boolean {
    if (task.status === 'done') return false;
    const diff = this.daysUntilDue(task);
    return diff !== null && diff >= 0 && diff <= 1;
  }

  // ---------- Yetki ----------

  /**
   * Kayıt olan herkes ekipsiz TeamMember olarak başlıyor ve sunucu ekipsiz kullanıcıya
   * boş liste dönüyor. Önceden ekran hiçbir açıklama olmadan bomboş kalıyor, kullanıcı
   * ne yapması gerektiğini anlayamıyordu.
   */
  get hasNoTeam(): boolean {
    return !this.authService.isAdmin() && this.authService.getTeamId() === null;
  }

  get canManageTasks(): boolean {
    const role = this.authService.getRole();
    return role === 'Admin' || role === 'TeamLeader';
  }

  canDrag(task: TaskItem): boolean {
    return this.canManageTasks || task.assignedToId === this.authService.getUserId();
  }

  // ---------- Sürükle-bırak ----------

  /**
   * Sıralama, başkalarının kartlarının da yerini değiştirdiği için yalnızca yöneticilere
   * açık. Ayrıca filtre açıkken ekrandaki sıra ile sütunun gerçek sırası aynı olmadığından
   * sıralama kapatılıyor - aksi halde görünmeyen kartlar rastgele yerlere kayardı.
   */
  get canReorder(): boolean {
    return this.canManageTasks && !this.hasActiveFilters;
  }

  drop(event: CdkDragDrop<TaskItem[]>, newStatus: string): void {
    const task = event.item.data as TaskItem;
    if (!task) return;

    this.reorderHint = '';

    // --- Aynı sütun içinde sıralama ---
    if (event.previousContainer === event.container) {
      if (event.previousIndex === event.currentIndex) return;

      if (!this.canReorder) {
        this.reorderHint = this.canManageTasks
          ? 'Sıralama için önce filtreleri temizleyin.'
          : 'Görevleri yalnızca ekip lideri sıralayabilir.';
        this.cdr.markForCheck();
        return;
      }

      const column = this.tasksFor(newStatus);
      moveItemInArray(column, event.previousIndex, event.currentIndex);
      this.cdr.markForCheck();
      this.persistOrder(newStatus);
      return;
    }

    // --- Sütunlar arası taşıma ---
    const previousStatus = task.status;
    this.removeFromColumn(task, previousStatus);
    task.status = newStatus;

    const destination = this.tasksFor(newStatus);
    if (this.canReorder) {
      destination.splice(Math.min(event.currentIndex, destination.length), 0, task);
    } else {
      destination.push(task);
    }
    this.cdr.markForCheck();

    if (this.canReorder) {
      // Tek istekte hem durumu hem sırayı kaydediyoruz.
      this.persistOrder(newStatus);
    } else {
      this.taskService.updateTaskStatus(task.id, newStatus).subscribe({
        error: () => {
          this.errorMessage = 'Görev taşınırken bir hata oluştu, tekrar deneyin.';
          this.loadTasks();
        }
      });
    }
  }

  private persistOrder(status: string): void {
    const orderedIds = this.tasksFor(status).map(t => t.id);
    this.taskService.reorder(status, orderedIds).subscribe({
      error: () => {
        this.errorMessage = 'Sıralama kaydedilemedi, tekrar deneyin.';
        this.loadTasks();
      }
    });
  }

  private removeFromColumn(task: TaskItem, status: string): void {
    const column = this.tasksFor(status);
    const index = column.findIndex(t => t.id === task.id);
    if (index !== -1) column.splice(index, 1);
  }

  private addToColumn(task: TaskItem, status: string): void {
    this.tasksFor(status).push(task);
  }

  // ---------- Modal ----------

  /** Artı düğmesi artık her sütunda var; açılan pencerede durum o sütuna göre
   *  önceden seçili geliyor, kullanıcı ekleyip sonra sürüklemek zorunda kalmıyor. */
  openAddModal(status: string = 'todo'): void {
    if (!this.canManageTasks) return;
    this.editingTask = null;
    this.modalInitialStatus = status;
    this.modalOpen = true;
  }

  openEditModal(task: TaskItem): void {
    this.editingTask = task;
    this.modalOpen = true;
  }

  onModalClosed(shouldRefresh: boolean): void {
    this.modalOpen = false;
    this.editingTask = null;
    if (shouldRefresh) this.loadTasks();
    this.cdr.markForCheck();
  }
}
