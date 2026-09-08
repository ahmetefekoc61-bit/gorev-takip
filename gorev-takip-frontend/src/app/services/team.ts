import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface Team {
  id: number;
  name: string;
}

@Injectable({
  providedIn: 'root'
})
export class TeamService {
  private apiUrl = 'https://localhost:7236/api/teams';

  constructor(private http: HttpClient) { }

  getTeams(): Observable<Team[]> {
    return this.http.get<Team[]>(this.apiUrl);
  }

  createTeam(name: string): Observable<Team> {
    return this.http.post<Team>(this.apiUrl, { name });
  }

  updateTeam(id: number, name: string): Observable<void> {
    return this.http.put<void>(`${this.apiUrl}/${id}`, { id, name });
  }

  deleteTeam(id: number): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${id}`);
  }
}
