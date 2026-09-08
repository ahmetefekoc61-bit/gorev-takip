import { ChangeDetectorRef, Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';
import { ProjectService, Project } from '../services/project';
import { TaskService, TaskItem } from '../services/task';
import { TeamService, Team } from '../services/team';
import { TaskModal } from '../task-modal/task-modal';

/**
 * Bir projeye tıklanınca açılan detay sayfası: ilerleme yüzdesi, durum bazlı
 * dağılım ve o projeye ait görevlerin listesi. Bir görev satırına tıklayınca
 * board'daki ile aynı modal açılır - iki yerden de aynı düzenleme deneyimi.
 */
@Component({
  selector: 'app-project-detail',
  imports: [CommonModule, RouterLink, TaskModal],
  templateUrl: './project-detail.html',
  styleUrl: './project-detail.css'
})
export class ProjectDetail implements OnInit {
  loading = true;
  errorMessage = '';

  project: Project | null = null;
  teamName = '';
  tasks: TaskItem[] = [];

  modalOpen = false;
  editingTask: TaskItem | null = null;

  readonly circumference = 2 * Math.PI * 45;

  readonly priorityLabels: Record<string, string> = {
    Low: 'Düşük',
    Medium: 'Orta',
    High: 'Yüksek',
    Critical: 'Kritik'
  };

  constructor(
    private route: ActivatedRoute,
    private projectService: ProjectService,
    private taskService: TaskService,
    private teamService: TeamService,
    private cdr: ChangeDetectorRef
  ) { }

  get total(): number {
    return this.tasks.length;
  }

  get todoCount(): number {
    return this.tasks.filter(t => t.status === 'todo').length;
  }

  get inProgressCount(): number {
    return this.tasks.filter(t => t.status === 'in_progress').length;
  }

  get doneCount(): number {
    return this.tasks.filter(t => t.status === 'done').length;
  }

  get completionRate(): number {
    return this.total === 0 ? 0 : Math.round((this.doneCount / this.total) * 100);
  }

  get overdueCount(): number {
    const today = new Date();
    today.setHours(0, 0, 0, 0);
    return this.tasks.filter(t => t.dueDate && t.status !== 'done' && new Date(t.dueDate) < today).length;
  }

  /** İlerleme donut'u için tek segment (tamamlanma yüzdesi). */
  dashArray(pct: number): string {
    const length = (pct / 100) * this.circumference;
    return `${length} ${this.circumference - length}`;
  }

  ngOnInit(): void {
    const id = Number(this.route.snapshot.paramMap.get('id'));
    this.load(id);
  }

  load(id: number): void {
    this.loading = true;
    forkJoin({
      project: this.projectService.getProject(id),
      tasks: this.taskService.getTasks(),
      teams: this.teamService.getTeams()
    }).subscribe({
      next: ({ project, tasks, teams }) => {
        this.project = project;
        this.tasks = tasks.filter(t => t.projectId === id);
        this.teamName = teams.find((t: Team) => t.id === project.teamId)?.name ?? '—';
        this.loading = false;
        this.cdr.markForCheck();
      },
      error: () => {
        this.loading = false;
        this.errorMessage = 'Proje yüklenirken bir hata oluştu.';
        this.cdr.markForCheck();
      }
    });
  }

  statusLabel(status: string): string {
    return status === 'todo' ? 'Yapılacak' : status === 'in_progress' ? 'Devam Ediyor' : 'Tamamlandı';
  }

  priorityLabel(priority: string): string {
    return this.priorityLabels[priority] ?? priority;
  }

  isOverdue(task: TaskItem): boolean {
    if (!task.dueDate || task.status === 'done') return false;
    const today = new Date();
    today.setHours(0, 0, 0, 0);
    return new Date(task.dueDate) < today;
  }

  openTask(task: TaskItem): void {
    this.editingTask = task;
    this.modalOpen = true;
  }

  onModalClosed(shouldRefresh: boolean): void {
    this.modalOpen = false;
    this.editingTask = null;
    if (shouldRefresh && this.project) {
      this.load(this.project.id);
    }
    this.cdr.markForCheck();
  }
}
