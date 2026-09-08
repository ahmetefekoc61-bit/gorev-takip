import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface Project {
  id: number;
  name: string;
  teamId: number;
}

@Injectable({
  providedIn: 'root'
})
export class ProjectService {
  private apiUrl = 'https://localhost:7236/api/projects';

  constructor(private http: HttpClient) { }

  getProjects(): Observable<Project[]> {
    return this.http.get<Project[]>(this.apiUrl);
  }

  getProject(id: number): Observable<Project> {
    return this.http.get<Project>(`${this.apiUrl}/${id}`);
  }

  /**
   * Yeni proje oluşturur. teamId sadece Admin için anlamlı - backend, admin olmayan
   * kullanıcı için gönderilen teamId'yi zaten yok sayıp kendi ekibini atıyor.
   */
  createProject(name: string, teamId?: number): Observable<Project> {
    const body = teamId !== undefined ? { name, teamId } : { name };
    return this.http.post<Project>(this.apiUrl, body);
  }
}
