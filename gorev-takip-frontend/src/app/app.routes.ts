import { Routes } from '@angular/router';
import { Login } from './login/login';
import { Board } from './board/board';
import { ForgotPassword } from './forgot-password/forgot-password';
import { ResetPassword } from './reset-password/reset-password';
import { Projects } from './projects/projects';
import { ProjectDetail } from './project-detail/project-detail';
import { Teams } from './teams/teams';
import { Dashboard } from './dashboard/dashboard';
import { TaskDetail } from './task-detail/task-detail';
import { MyWork } from './my-work/my-work';
import { Layout } from './shared/layout/layout';
import { authGuard } from './guards/auth-guard';

export const routes: Routes = [
  { path: 'login', component: Login },
  { path: 'forgot-password', component: ForgotPassword },
  { path: 'reset-password', component: ResetPassword },
  {
    path: '',
    component: Layout,
    canActivate: [authGuard],
    children: [
      { path: 'dashboard', component: Dashboard },
      { path: 'board', component: Board },
      { path: 'my-work', component: MyWork },
      // Görevin kendi adresi: paylaşılabiliyor, sekmede açık kalabiliyor,
      // bildirimden doğrudan buraya gelinebiliyor.
      { path: 'tasks/:id', component: TaskDetail },
      { path: 'projects', component: Projects },
      { path: 'projects/:id', component: ProjectDetail },
      { path: 'teams', component: Teams },
      { path: '', redirectTo: 'board', pathMatch: 'full' }
    ]
  },
  { path: '**', redirectTo: 'login' }
];
