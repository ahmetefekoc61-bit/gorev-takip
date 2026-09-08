import { ChangeDetectorRef, Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { Router } from '@angular/router';
import { AuthService } from '../../services/auth';
import { UserDirectoryService, UserRole, UserSummary, resolveAvatarUrl } from '../../services/user-directory';
import { TaskService } from '../../services/task';
import { TeamService } from '../../services/team';

@Component({
  selector: 'app-sidebar',
  imports: [CommonModule, RouterLink, RouterLinkActive],
  templateUrl: './sidebar.html',
  styleUrl: './sidebar.css'
})
export class Sidebar implements OnInit {
  currentUser: UserSummary | null = null;
  teamName: string | null = null;

  // Pano linkindeki rozet: bana atanmış, henüz tamamlanmamış görev sayısı.
  // Sidebar salt navigasyon değil, işe hemen bakabileceğin bir özet olsun diye.
  myOpenTaskCount = 0;

  /** "Benim İşlerim" rozetinde gösterilen, bitiş tarihi geçmiş kendi görevlerimin sayısı. */
  myOverdueCount = 0;

  // "Ekibim" kartı: kendim hariç takım arkadaşlarım (footer'da zaten kendim görünüyor).
  teammates: UserSummary[] = [];
  private static readonly MAX_TEAMMATE_AVATARS = 5;

  // Ekip genelinde görev tamamlanma oranı - mini bir ilerleme çubuğu için.
  private teamTasksTotal = 0;
  private teamTasksDone = 0;

  private readonly roleLabels: Record<UserRole, string> = {
    Admin: 'Yönetici',
    TeamLeader: 'Ekip Lideri',
    TeamMember: 'Ekip Üyesi'
  };

  constructor(
    private authService: AuthService,
    private userDirectory: UserDirectoryService,
    private taskService: TaskService,
    private teamService: TeamService,
    private router: Router,
    private cdr: ChangeDetectorRef
  ) { }

  ngOnInit(): void {
    const myId = this.authService.getUserId();
    if (myId === null) return;

    this.userDirectory.getUsers().subscribe({
      next: (users) => {
        this.currentUser = users.find(u => u.id === myId) ?? null;
        const teamId = this.currentUser?.teamId ?? null;
        this.teammates = teamId === null ? [] : users.filter(u => u.teamId === teamId && u.id !== myId);
        this.cdr.markForCheck();

        if (teamId !== null) {
          this.teamService.getTeams().subscribe({
            next: (teams) => {
              this.teamName = teams.find(t => t.id === teamId)?.name ?? null;
              this.cdr.markForCheck();
            },
            error: () => { /* ekip adı gelmezse menü ekipsiz görünür, akış bozulmaz */ }
          });
        }
      },
      // Hata dalı olmadığında istek başarısız olduğunda hiçbir şey olmuyor, panel
      // yükleniyor gibi asılı kalıyordu. En azından bilinen bir duruma düşürüyoruz.
      error: () => {
        this.currentUser = null;
        this.teammates = [];
        this.cdr.markForCheck();
      }
    });

    this.taskService.getTasks().subscribe({
      next: (tasks) => {
        const today = new Date();
        today.setHours(0, 0, 0, 0);

        const myOpen = tasks.filter(t => t.assignedToId === myId && t.status !== 'done');
        this.myOpenTaskCount = myOpen.length;
        this.myOverdueCount = myOpen.filter(t => t.dueDate && new Date(t.dueDate) < today).length;
        this.teamTasksTotal = tasks.length;
        this.teamTasksDone = tasks.filter(t => t.status === 'done').length;
        this.cdr.markForCheck();
      },
      error: () => {
        this.myOpenTaskCount = 0;
        this.myOverdueCount = 0;
        this.teamTasksTotal = 0;
        this.teamTasksDone = 0;
        this.cdr.markForCheck();
      }
    });
  }

  get visibleTeammates(): UserSummary[] {
    return this.teammates.slice(0, Sidebar.MAX_TEAMMATE_AVATARS);
  }

  get extraTeammateCount(): number {
    return Math.max(0, this.teammates.length - Sidebar.MAX_TEAMMATE_AVATARS);
  }

  get teamCompletionRate(): number {
    return this.teamTasksTotal === 0 ? 0 : Math.round((this.teamTasksDone / this.teamTasksTotal) * 100);
  }

  logout(): void {
    this.authService.logout();
    this.router.navigate(['/login']);
  }

  initials(fullName: string): string {
    return fullName
      .split(' ')
      .filter(Boolean)
      .slice(0, 2)
      .map(part => part[0]?.toUpperCase())
      .join('');
  }

  roleLabel(role: UserRole): string {
    return this.roleLabels[role] ?? role;
  }

  avatarUrl(user: UserSummary): string | null {
    return resolveAvatarUrl(user.avatarUrl);
  }
}
