import { ChangeDetectorRef, Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ProjectService, Project } from '../services/project';
import { TeamService, Team } from '../services/team';
import { AuthService } from '../services/auth';
import { SearchService } from '../services/search';

interface ProjectRow extends Project {
  teamName: string;
}

@Component({
  selector: 'app-projects',
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './projects.html',
  styleUrl: './projects.css'
})
export class Projects implements OnInit {
  loading = true;
  errorMessage = '';
  projects: ProjectRow[] = [];
  teams: Team[] = [];
  searchTerm = '';

  newProjectName = '';
  newProjectTeamId: number | null = null;
  creating = false;

  constructor(
    private projectService: ProjectService,
    private teamService: TeamService,
    public authService: AuthService,
    private searchService: SearchService,
    private cdr: ChangeDetectorRef
  ) {
    // Topbar'daki arama kutusu ile bu sayfa arasındaki bağlantı - pano'daki aynı desen.
    this.searchService.term$.pipe(takeUntilDestroyed()).subscribe(term => {
      this.searchTerm = term;
      this.cdr.markForCheck();
    });
  }

  get filteredProjects(): ProjectRow[] {
    const term = this.searchTerm.trim().toLowerCase();
    if (!term) return this.projects;
    return this.projects.filter(p =>
      p.name.toLowerCase().includes(term) || p.teamName.toLowerCase().includes(term)
    );
  }

  /**
   * Proje ekleme sadece Admin ve Ekip Lideri'ne açık; sıradan ekip üyesi sadece görür.
   */
  get canCreate(): boolean {
    const role = this.authService.getRole();
    return role === 'Admin' || role === 'TeamLeader';
  }

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading = true;
    this.errorMessage = '';
    forkJoin({
      projects: this.projectService.getProjects(),
      teams: this.teamService.getTeams()
    }).subscribe({
      next: ({ projects, teams }) => {
        this.teams = teams;
        const teamNameById = new Map(teams.map(t => [t.id, t.name]));
        this.projects = projects.map(p => ({ ...p, teamName: teamNameById.get(p.teamId) ?? '—' }));

        if (this.authService.isAdmin() && this.newProjectTeamId === null && teams.length > 0) {
          this.newProjectTeamId = teams[0].id;
        }

        this.loading = false;
        this.cdr.markForCheck();
      },
      error: () => {
        this.loading = false;
        this.errorMessage = 'Projeler yüklenirken bir hata oluştu.';
        this.cdr.markForCheck();
      }
    });
  }

  createProject(): void {
    const name = this.newProjectName.trim();
    if (!name) return;
    if (this.authService.isAdmin() && this.newProjectTeamId === null) return;

    this.creating = true;
    const teamId = this.authService.isAdmin() ? this.newProjectTeamId! : undefined;

    this.projectService.createProject(name, teamId).subscribe({
      next: () => {
        this.creating = false;
        this.newProjectName = '';
        this.load();
      },
      error: () => {
        this.creating = false;
        this.errorMessage = 'Proje oluşturulurken bir hata oluştu.';
        this.cdr.markForCheck();
      }
    });
  }
}
